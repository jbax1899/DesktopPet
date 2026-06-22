namespace DesktopPet.App.Observation;

public interface IVisionObservationProvider
{
    VisionObservation? LatestObservation { get; }

    string? LatestThumbnailPath { get; }

    IReadOnlyList<ObservationRecord> RecentObservations { get; }
}
