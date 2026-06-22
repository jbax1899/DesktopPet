namespace DesktopPet.App.Errors;

public sealed class PetErrorException : Exception
{
    public PetErrorException(PetErrorCode code, string technicalMessage, Exception? innerException = null)
        : base(technicalMessage, innerException)
    {
        Error = new PetError(code, technicalMessage);
    }

    public PetError Error { get; }
}
