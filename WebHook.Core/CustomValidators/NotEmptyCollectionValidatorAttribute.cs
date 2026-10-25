using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.CustomValidators;

/// <summary>
/// Validates that a collection contains at least one item.
/// </summary>
internal class NotEmptyCollectionValidatorAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is IEnumerable enumObject)
        {
            foreach (var _ in enumObject)
            {
                return ValidationResult.Success;
            }
        }

        return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must contain at least one item.");

    }
}


/// <summary>
/// Validates that a dictionary contains at least one entry.
/// </summary>
internal class NotEmptyDictionaryCollectionAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is IDictionary dictionaryObject)
        {
            foreach (var item in dictionaryObject)
            {
                return ValidationResult.Success;
            }
        }

        return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must contain at least one item and a valid disctionary.");
    }
}