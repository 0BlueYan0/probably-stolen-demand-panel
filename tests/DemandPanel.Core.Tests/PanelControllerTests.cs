using DemandPanel.Core;
using Xunit;

public class PanelControllerTests
{
    private static CustomerSnapshot Eligible(object customer) => new CustomerSnapshot(customer, true, true);
    [Fact] public void Toggle_WithEligibleCustomer_Opens() { var p = new PanelController(); Assert.Equal(PanelCommand.Open, p.OnTogglePressed(Eligible(new object()))); Assert.True(p.IsOpen); }
    [Fact] public void Toggle_WhenOpen_Closes() { var p = new PanelController(); var c = Eligible(new object()); p.OnTogglePressed(c); Assert.Equal(PanelCommand.Close, p.OnTogglePressed(c)); Assert.False(p.IsOpen); }
    [Fact] public void Toggle_NoCustomer_ShowsNoCustomer() { var p = new PanelController(); Assert.Equal(PanelCommand.ShowNoCustomer, p.OnTogglePressed(Eligible(null))); Assert.False(p.IsOpen); }
    [Fact] public void Toggle_CustomerSellingOnly_ShowsNoCustomer() { var p = new PanelController(); Assert.Equal(PanelCommand.ShowNoCustomer, p.OnTogglePressed(new CustomerSnapshot(new object(), true, false))); Assert.False(p.IsOpen); }
    [Fact] public void Toggle_CustomerNotArrived_ShowsNoCustomer() { var p = new PanelController(); Assert.Equal(PanelCommand.ShowNoCustomer, p.OnTogglePressed(new CustomerSnapshot(new object(), false, true))); Assert.False(p.IsOpen); }
    [Fact] public void OnFrame_SameCustomer_None() { var p = new PanelController(); var c = Eligible(new object()); p.OnTogglePressed(c); Assert.Equal(PanelCommand.None, p.OnFrame(c)); Assert.True(p.IsOpen); }
    [Fact] public void OnFrame_CustomerLeft_Closes() { var p = new PanelController(); p.OnTogglePressed(Eligible(new object())); Assert.Equal(PanelCommand.Close, p.OnFrame(Eligible(null))); Assert.False(p.IsOpen); }
    [Fact] public void OnFrame_CustomerChanged_Closes() { var p = new PanelController(); p.OnTogglePressed(Eligible(new object())); Assert.Equal(PanelCommand.Close, p.OnFrame(Eligible(new object()))); Assert.False(p.IsOpen); }
    [Fact] public void OnFrame_WhenClosed_None() { var p = new PanelController(); Assert.Equal(PanelCommand.None, p.OnFrame(Eligible(new object()))); Assert.False(p.IsOpen); }
}
