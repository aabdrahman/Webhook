using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.CustomValidators;

internal sealed class CallBackUrlValidatorAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if(value is null)
        {
            return new ValidationResult("No Call Back Url provided.");
        }

        if(value is not string url)
        {
            return new ValidationResult("The provided value is not of string type.");
        }

        if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) 
        {
            return new ValidationResult("The provided call back url is not valid.");
        }

        if(uri.Scheme != Uri.UriSchemeHttps)
        {
            return new ValidationResult("Only HTTPS call back urls are supported.");
        }

        if (uri.IsLoopback)
        {
            return new ValidationResult("Loopback addresses ar enot supported.");
        }

        return ValidationResult.Success;
    }
}
