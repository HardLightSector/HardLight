using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.Piping.Trinary.Components
{
    [Serializable, NetSerializable]
    public enum GasFilterUiKey
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class GasFilterBoundUserInterfaceState : BoundUserInterfaceState
    {
        public string FilterLabel { get; }
        public float TransferRate { get; }
        public bool Enabled { get; }
        public Gas? FilteredGas { get; }
        public bool HighFlow { get; }
        public float MaxTransferRate { get; }
        public GasFilterBoundUserInterfaceState(string filterLabel, float transferRate, bool enabled, Gas? filteredGas, bool highFlow, float maxTransferRate)
        {
            FilterLabel = filterLabel;
            TransferRate = transferRate;
            Enabled = enabled;
            FilteredGas = filteredGas;
            HighFlow = highFlow;
            MaxTransferRate = maxTransferRate;
        }
    }

    [Serializable, NetSerializable]
    public sealed class GasFilterToggleStatusMessage : BoundUserInterfaceMessage
    {
        public bool Enabled { get; }

        public GasFilterToggleStatusMessage(bool enabled)
        {
            Enabled = enabled;
        }
    }

    [Serializable, NetSerializable]
    public sealed class GasFilterChangeRateMessage : BoundUserInterfaceMessage
    {
        public float Rate { get; }

        public GasFilterChangeRateMessage(float rate)
        {
            Rate = rate;
        }
    }

    [Serializable, NetSerializable]
    public sealed class GasFilterSelectGasMessage : BoundUserInterfaceMessage
    {
        public int? ID { get; }

        public GasFilterSelectGasMessage(int? id)
        {
            ID = id;
        }
    }
}
