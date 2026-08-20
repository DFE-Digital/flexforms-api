using Microsoft.AspNetCore.Http.Extensions;

namespace GovUK.Dfe.FlexForms.Api.Middleware
{
    /// <summary>
    /// Applies an additional URI unescape to query values (for double-encoded clients)
    /// without treating '+' as a space the way <c>HttpUtility.UrlDecode</c> / form decoding does.
    /// </summary>
    public class UrlDecoderMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.QueryString.HasValue)
            {
                await next(context);
                return;
            }

            // Use already-parsed query values (ASP.NET has correctly turned %2B into '+'),
            // then unescape once more for double-encoded values. Do not use HttpUtility.UrlDecode
            // on the raw query string: that turns %2B → '+' and a subsequent form parse turns '+' → ' '.
            var items = context.Request.Query
                .SelectMany(
                    pair => pair.Value,
                    (pair, value) => new KeyValuePair<string, string>(pair.Key, DecodeQueryValue(value)))
                .ToList();

            context.Request.QueryString = new QueryBuilder(items).ToQueryString();

            await next(context);
        }

        private static string DecodeQueryValue(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }
    }
}
