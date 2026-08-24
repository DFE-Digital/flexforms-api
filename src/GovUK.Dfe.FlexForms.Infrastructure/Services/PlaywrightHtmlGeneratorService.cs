using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Service that uses Playwright to generate static HTML snapshots of dynamically rendered pages
/// </summary>
[ExcludeFromCodeCoverage]
public class PlaywrightHtmlGeneratorService(ILogger<PlaywrightHtmlGeneratorService> logger) : IStaticHtmlGeneratorService
{
    private readonly ILogger<PlaywrightHtmlGeneratorService> _logger = logger;

    /// <summary>
    /// Generates a static HTML snapshot of a web page using Playwright
    /// </summary>
    /// <param name="previewUrl">The full URL of the page to capture</param>
    /// <param name="authenticationHeaders">Authentication headers to pass to the browser for authenticated access</param>
    /// <param name="contentSelector">Optional CSS selector to extract only a specific section of the page (e.g., ".govuk-grid-row")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation that returns the static HTML content as a string</returns>
    public async Task<string> GenerateStaticHtmlAsync(
        string previewUrl,
        IDictionary<string, string> authenticationHeaders,
        string? contentSelector = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting static HTML generation for URL: {PreviewUrl}", previewUrl);
            using var playwright = await Playwright.CreateAsync();

            _logger.LogInformation("Launching Chromium browser in headless mode");
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                // Production-ready browser args for containerized environments
                Args = new[]
                {
                    "--disable-dev-shm-usage", // Overcome limited resource problems in containers
                    "--no-sandbox",             // Required for running in containers
                    "--disable-setuid-sandbox", // Required for running in containers
                    "--disable-gpu"             // Not needed in headless mode
                }
            });

            // Create a new browser context
            _logger.LogInformation("Creating browser context with authentication headers");
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",

                // Accept all self-signed certificates for local development
                IgnoreHTTPSErrors = true,
                // Add authentication headers to all requests
                ExtraHTTPHeaders = authenticationHeaders
            });

            _logger.LogInformation("Added {Count} authentication headers", authenticationHeaders.Count);
            foreach (var header in authenticationHeaders)
            {
                _logger.LogDebug("Added authentication header: {HeaderName}", header.Key);
            }

            var page = await context.NewPageAsync();

            _logger.LogInformation("Navigating to preview URL: {PreviewUrl}", previewUrl);
            await page.GotoAsync(previewUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 1200000 // 60 seconds timeout
            });

            _logger.LogInformation("Waiting for page to be fully loaded");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Additional wait to ensure all dynamic content is rendered
            await Task.Delay(2000, cancellationToken);

            _logger.LogInformation("Inlining CSS styles and resources into HTML");

            // Inline all external stylesheets, images, and resources into the HTML for a self-contained file
            await page.EvaluateAsync(@"
                async () => {
                    // Function to inline external stylesheets
                    const inlineStylesheets = async () => {
                        const links = Array.from(document.querySelectorAll('link[rel=""stylesheet""]'));
                        
                        for (const link of links) {
                            try {
                                const href = link.href;
                                if (!href) continue;
                                
                                // Fetch the stylesheet content
                                const response = await fetch(href);
                                const cssText = await response.text();
                                
                                // Create a style element with the CSS content
                                const style = document.createElement('style');
                                style.textContent = cssText;
                                style.setAttribute('data-source', href);
                                
                                // Insert the style element before the link
                                link.parentNode.insertBefore(style, link);
                                
                                // Remove the original link element
                                link.remove();
                            } catch (e) {
                                console.warn('Failed to inline stylesheet:', link.href, e);
                            }
                        }
                    };
                    
                    // Function to inline images as base64 data URIs
                    const inlineImages = async () => {
                        const images = Array.from(document.querySelectorAll('img[src]'));
                        
                        for (const img of images) {
                            try {
                                const src = img.src;
                                if (!src || src.startsWith('data:')) continue; // Skip if already data URI
                                
                                // Fetch the image
                                const response = await fetch(src);
                                const blob = await response.blob();
                                
                                // Convert to base64 data URI
                                const reader = new FileReader();
                                const dataUri = await new Promise((resolve) => {
                                    reader.onloadend = () => resolve(reader.result);
                                    reader.readAsDataURL(blob);
                                });
                                
                                // Replace src with data URI
                                img.src = dataUri;
                            } catch (e) {
                                console.warn('Failed to inline image:', img.src, e);
                            }
                        }
                    };
                    
                    await inlineStylesheets();
                    await inlineImages();
                }
            ");

            _logger.LogInformation("Capturing page content with inlined styles");

            string html;

            // If a content selector is provided, extract only that section
            if (!string.IsNullOrEmpty(contentSelector))
            {
                _logger.LogInformation("Extracting content using selector: {ContentSelector}", contentSelector);

                html = await page.EvaluateAsync<string>(@"
                    (selector) => {
                        // Get all inlined styles
                        const styles = Array.from(document.querySelectorAll('style'))
                            .map(s => s.outerHTML)
                            .join('\n');
                        
                        // Get the selected content
                        const content = document.querySelector(selector);
                        if (!content) {
                            throw new Error('Content selector not found: ' + selector);
                        }
                        
                        // Create a clean HTML document with the extracted content
                        return `<!DOCTYPE html>
                        <html lang=""en"" class=""govuk-template"">
                        <head>
                            <meta charset=""utf-8"">
                            <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
                            <title>Application Preview</title>
                            ${styles}
                        </head>
                        <body class=""govuk-template__body"">
                            <div class=""govuk-width-container"">
                                ${content.outerHTML}
                            </div>
                        </body>
                        </html>`;
                    }
                ", contentSelector);

                _logger.LogInformation("Successfully extracted content section");
            }
            else
            {
                // Capture the entire page
                html = await page.ContentAsync();
            }

            _logger.LogInformation("Static HTML generation completed successfully");
            
            // Post-process the HTML to remove tokens and update file download links
            html = PostProcessHtml(html, contentSelector);
            
            return html;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while generating static HTML for URL: {PreviewUrl}", previewUrl);
            throw;
        }
    }

    /// <summary>
    /// Post-processes the HTML to remove anti-forgery tokens and update file download form actions
    /// </summary>
    /// <param name="html">The raw HTML content</param>
    /// <param name="contentSelector">The content selector</param>
    /// <returns>The processed HTML content</returns>
    private string PostProcessHtml(string html, string? contentSelector = null)
    {
        _logger.LogInformation("Post-processing HTML content");

        // 1. Remove all anti-forgery token input fields
        // Pattern matches: <input name="__RequestVerificationToken" type="hidden" value="..." />
        var tokenPattern = @"<input[^>]*name=""__RequestVerificationToken""[^>]*>";
        html = Regex.Replace(html, tokenPattern, string.Empty, RegexOptions.IgnoreCase);
        _logger.LogDebug("Removed anti-forgery tokens from HTML");

        // 2. Update file download form actions
        // Pattern matches: action="/applications/{ref}/...?handler=DownloadFile"
        // Replace with: action="/DownloadEatFile"
        var actionPattern = @"action=""[^""]*\?handler=DownloadFile""";
        html = Regex.Replace(html, actionPattern, @"action=""DownloadEatFile""", RegexOptions.IgnoreCase);
        _logger.LogDebug("Updated file download form actions");

        // 3. Remove the preview page heading (default or tenant-customised copy)
        var headingPattern = @"<h1[^>]*class=""[^""]*govuk-heading-xl[^""]*""[^>]*>[\s\S]*?</h1>";
        html = Regex.Replace(html, headingPattern, string.Empty, RegexOptions.IgnoreCase);
        _logger.LogDebug("Removed preview page heading from HTML");

        // 4. Remove the content selector class
        if (!string.IsNullOrEmpty(contentSelector))
        {
            contentSelector = contentSelector.Replace(".", "");
            html = html.Replace(contentSelector, "eat-app-preview");
            _logger.LogDebug($"Removed '{contentSelector}' from the HTML");
        }

        _logger.LogInformation("HTML post-processing completed");
        return html;
    }
}

