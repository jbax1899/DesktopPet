namespace DesktopPet.App.Errors;

public sealed record PetError(PetErrorCode Code, string TechnicalMessage)
{
    public static PetError FromException(Exception exception, PetErrorCode fallbackCode)
    {
        return exception is PetErrorException petErrorException
            ? petErrorException.Error
            : new PetError(fallbackCode, exception.Message);
    }
}
