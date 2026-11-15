namespace EmberKnights.Archipelago;

/// <summary>
/// Counts distinct rooms entered per world during the current run.
///
/// The depth counter restarts whenever the player enters a different world or returns to the Nexus,
/// so each run starts again at room 1. Depth is only tracked for the five base-game worlds.
/// </summary>
public sealed class RoomProgressTracker
{
    public const int FirstWorld = 1;
    public const int LastWorld = 5;
    public const int NexusArea = 6;
    private const int LevelExitRoomType = 16;
    private const int NoRoomType = 0;

    private readonly HashSet<int> visitedRooms = new();
    private int currentArea;

    /// <summary>The number of distinct rooms entered in the current world during this run.</summary>
    public int Depth => visitedRooms.Count;

    /// <summary>
    /// Registers a room entry and returns the depth reached in <paramref name="areaName"/>,
    /// or 0 when the area is not one of the five base worlds.
    /// </summary>
    public int Enter(int areaName, int roomInstanceId, int roomType) => Enter(areaName, roomInstanceId, roomType, dlcActive: false);

    /// <summary>
    /// Same as <see cref="Enter(int,int,int)"/>, and when <paramref name="dlcActive"/> is true the four DLC worlds and
    /// the Architect level count as worlds too. With it false the behavior is exactly what it always was.
    /// </summary>
    public int Enter(int areaName, int roomInstanceId, int roomType, bool dlcActive)
    {
        if (areaName != currentArea)
        {
            // New world, or the Nexus between runs: depth starts over.
            visitedRooms.Clear();
            currentArea = areaName;
        }

        if (!DlcPolicy.IsTrackedWorld(areaName, dlcActive))
            return 0;

        // Exit portals and unset room types are transitions, not rooms the player explores. Every other
        // room type counts so an unexpected type can never make a milestone unreachable.
        if (roomType != NoRoomType && roomType != LevelExitRoomType)
            visitedRooms.Add(roomInstanceId);

        return visitedRooms.Count;
    }

    public void Reset()
    {
        visitedRooms.Clear();
        currentArea = 0;
    }
}
