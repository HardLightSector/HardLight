namespace Content.Shared.StationRecords;

public abstract class SharedStationRecordsSystem : EntitySystem
{
    public StationRecordKey? Convert((NetEntity, uint)? input)
    {
        return input == null ? null : Convert(input.Value);
    }

    public (NetEntity, uint)? Convert(StationRecordKey? input)
    {
        return input == null ? null : Convert(input.Value);
    }

    public StationRecordKey Convert((NetEntity, uint) input)
    {
        return new StationRecordKey(input.Item2, GetEntity(input.Item1));
    }
    public (NetEntity, uint) Convert(StationRecordKey input)
    {
        // Use TryGetNetEntity to avoid errors when the origin station entity is deleted/invalid
        if (!TryGetNetEntity(input.OriginStation, out var netEntity))
            netEntity = NetEntity.Invalid;

        return (netEntity.Value, input.Id);
    }

    public List<(NetEntity, uint)> Convert(ICollection<StationRecordKey> input)
    {
        var result = new List<(NetEntity, uint)>(input.Count);
        foreach (var entry in input)
        {
            result.Add(Convert(entry));
        }
        return result;
    }

    public List<StationRecordKey> Convert(ICollection<(NetEntity, uint)> input)
    {
        var result = new List<StationRecordKey>(input.Count);
        foreach (var entry in input)
        {
            result.Add(Convert(entry));
        }
        return result;
    }
}
