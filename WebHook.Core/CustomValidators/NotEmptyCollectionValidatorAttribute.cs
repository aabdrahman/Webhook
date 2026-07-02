using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.CustomValidators;

internal class NotEmptyCollectionValidatorAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if(value is IEnumerable enumObject)
        {
            foreach(var _ in enumObject)
            {
                return ValidationResult.Success;
            }
        }

        return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must contain at least one item.");

    }
}


internal class NotEmptyDictionaryCollectionAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if(value is IDictionary dictionaryObject)
        {
            foreach (var item in dictionaryObject)
            {
                return ValidationResult.Success;
            }
        }

        return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must contain at least one item and a valid disctionary.");
    }
}