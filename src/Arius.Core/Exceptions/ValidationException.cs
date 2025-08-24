using FluentValidation.Results;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Arius.Core.Exceptions;

public class ValidationException : Exception
{
    public IReadOnlyList<ValidationFailure> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base(BuildErrorMessage(failures))
    {
        Errors = failures.ToList().AsReadOnly();
    }

    private static string BuildErrorMessage(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures.Select(f => f.ErrorMessage);
        return string.Join(Environment.NewLine, errors);
    }
}