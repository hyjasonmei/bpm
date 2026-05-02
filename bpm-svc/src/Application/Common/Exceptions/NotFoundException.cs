namespace Bpm.Application.Common.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}
