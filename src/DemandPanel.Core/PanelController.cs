namespace DemandPanel.Core
{
    public readonly struct CustomerSnapshot
    {
        public CustomerSnapshot(object customer, bool arrived, bool buying) { Customer = customer; Arrived = arrived; Buying = buying; }
        public object Customer { get; }
        public bool Arrived { get; }
        public bool Buying { get; }
        public bool Eligible => Customer != null && Arrived && Buying;
    }

    public enum PanelCommand { None, Open, Close, ShowNoCustomer }

    public sealed class PanelController
    {
        private object customer;
        public bool IsOpen { get; private set; }

        public PanelCommand OnTogglePressed(CustomerSnapshot c)
        {
            if (IsOpen) return Close();
            if (!c.Eligible) return PanelCommand.ShowNoCustomer;
            customer = c.Customer;
            IsOpen = true;
            return PanelCommand.Open;
        }

        public PanelCommand OnFrame(CustomerSnapshot c)
        {
            if (IsOpen && (!c.Eligible || !object.ReferenceEquals(c.Customer, customer))) return Close();
            return PanelCommand.None;
        }

        private PanelCommand Close()
        {
            IsOpen = false;
            customer = null;
            return PanelCommand.Close;
        }
    }
}
