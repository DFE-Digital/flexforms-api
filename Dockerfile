ARG DOTNET_VERSION=10.0

# ============================================================
# Stage 1 - Build + Install Playwright (Ubuntu SDK)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build
WORKDIR /build
ARG CI
ARG APP_VERSION=0.0.0-local
ENV CI=${CI}

# Install Playwright CLI
RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copy solution
COPY ./src/ ./src/
COPY Directory.Build.props ./
COPY GovUK.Dfe.FlexForms.Api.sln ./
COPY script/ ./script/

# Restore + build (Api pulls Infrastructure so EF --no-build can bundle both contexts)
RUN dotnet restore GovUK.Dfe.FlexForms.Api.sln
RUN dotnet build ./src/GovUK.Dfe.FlexForms.Infrastructure -c Release --no-restore
RUN dotnet build ./src/GovUK.Dfe.FlexForms.Api -c Release --no-restore -p:Version=${APP_VERSION} -p:InformationalVersion=${APP_VERSION}

# Install Playwright browsers + OS dependencies (Ubuntu!)
RUN playwright install --with-deps

# Publish final output
RUN dotnet publish ./src/GovUK.Dfe.FlexForms.Api -c Release --no-build -o /app


# ============================================================
# Stage 2 - EF Migration Builder
# ============================================================
FROM build AS efbuilder
WORKDIR /build

ENV PATH=$PATH:/root/.dotnet/tools
ENV DOTNET_ROOT=/usr/share/dotnet

RUN dotnet tool install --global dotnet-ef --version 10.*

RUN mkdir /sql

# Bundle each DbContext separately. Use Infrastructure (migrations + design-time
# factories) so Program.cs is not started and no live SQL is required at build time.
RUN dotnet ef migrations bundle -r linux-x64 \
      --configuration Release \
      --project ./src/GovUK.Dfe.FlexForms.Infrastructure \
      --context ExternalApplicationsContext \
      --output /sql/migratedb-ea \
      --no-build \
      --self-contained

RUN dotnet ef migrations bundle -r linux-x64 \
      --configuration Release \
      --project ./src/GovUK.Dfe.FlexForms.Infrastructure \
      --context TenantConfigDbContext \
      --output /sql/migratedb-tenantconfig \
      --no-build \
      --self-contained

COPY script/migrate-databases.sh /sql/migratedb
RUN sed -i 's/\r$//' /sql/migratedb \
 && chmod +x /sql/migratedb /sql/migratedb-ea /sql/migratedb-tenantconfig


# ============================================================
# Stage 3 - Init Container (Keeps Azure Linux if needed)
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-azurelinux3.0 AS initcontainer
WORKDIR /sql

COPY --from=efbuilder /sql /sql
COPY --from=build /app/appsettings* /sql/
COPY --from=build /app/appsettings* /GovUK.Dfe.FlexForms.Api/

# Default command matches existing Azure init_container_command = ["/sql/migratedb"]
CMD ["/sql/migratedb"]


# ============================================================
# Stage 4 - Final Runtime (Ubuntu) + Playwright Runtime Support
# ============================================================

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble AS final
WORKDIR /app

# Install Playwright required system dependencies
RUN apt-get update && \
    apt-get install -y \
        libnss3 \
        libatk1.0-0t64 \
        libatk-bridge2.0-0t64 \
        libcups2t64 \
        libdbus-1-3 \
        libdrm2 \
        libxcomposite1 \
        libxdamage1 \
        libxrandr2 \
        libgbm1 \
        libasound2t64 \
        libxshmfence1 \
        libxkbcommon0 \
        libxext6 \
        libxfixes3 \
        libx11-6 \
        libx11-xcb1 \
        libglib2.0-0t64 \
        libgl1 \
        libpango-1.0-0 \
        libpangocairo-1.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Copy app + Playwright browsers
COPY --from=build /app /app
COPY --from=build /root/.cache/ms-playwright /home/app/.cache/ms-playwright
RUN chmod -R 755 /home/app/.cache/ms-playwright

COPY script/api-docker-entrypoint.sh /app/docker-entrypoint.sh
RUN sed -i 's/\r$//' /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

ENV ASPNETCORE_URLS=http://+:8080

USER $APP_UID
ENTRYPOINT ["/app/docker-entrypoint.sh"]
