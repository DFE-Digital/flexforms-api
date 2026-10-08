using GovUK.Dfe.CoreLibs.Http.Interfaces;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using System.Diagnostics.CodeAnalysis;

namespace GovUK.Dfe.FlexForms.Api.ExceptionHandlers
{
    [ExcludeFromCodeCoverage]
    public class ValidationExceptionHandler : ICustomExceptionHandler
    {
        public bool CanHandle(Type exceptionType) => exceptionType == typeof(ValidationException);

        public ExceptionResponse Handle(Exception exception, Dictionary<string, object>? context = null)
        {
            var validationException = (ValidationException)exception;

            var response = new ExceptionResponse
            {
                StatusCode = 400,
                Message = "Validation failed. Please check the following errors:",
                Details = FormatValidationErrors(validationException.Errors),
                ExceptionType = "ValidationException",
                // Add structured validation errors to context
                Context = new Dictionary<string, object>
                {
                    ["validationErrors"] = validationException.Errors,
                    ["errorCount"] = validationException.Errors.Count,
                    ["totalErrorCount"] = validationException.Errors.Values.Sum(errors => errors.Length)
                }
            };

            return response;
        }

        public int Priority => 10; // Higher priority than default handlers

        private string FormatValidationErrors(IDictionary<string, string[]> errors)
        {
            var formattedErrors = errors
                .SelectMany(kvp => kvp.Value.Select(error => string.IsNullOrEmpty(kvp.Key) ? error : $"{kvp.Key}: {error}"))
                .ToList();

            return string.Join("; ", formattedErrors);
        }
    }
}
