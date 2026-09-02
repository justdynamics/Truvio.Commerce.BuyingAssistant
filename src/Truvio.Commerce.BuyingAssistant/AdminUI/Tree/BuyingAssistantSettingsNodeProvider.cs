using Dynamicweb.Apps.UI;
using Dynamicweb.CoreUI.Actions.Implementations;
using Dynamicweb.CoreUI.Icons;
using Dynamicweb.CoreUI.Navigation;
using Truvio.Commerce.BuyingAssistant.AdminUI.Queries;
using Truvio.Commerce.BuyingAssistant.AdminUI.Screens;
using Truvio.Commerce.BuyingAssistant.AdminUI.Security;

namespace Truvio.Commerce.BuyingAssistant.AdminUI.Tree;

/// <summary>Hangs the "Buying Assistant" node under Settings, Apps (DW's own place for app settings).</summary>
public sealed class BuyingAssistantSettingsNodeProvider : NavigationNodeProvider<AppsSettingSection>
{
    public const string NodeId = "Truvio_BuyingAssistant";

    public override IEnumerable<NavigationNode> GetRootNodes()
    {
        if (!BuyingAssistantAccess.CanViewSettings()) yield break;
        yield return new NavigationNode
        {
            Id = NodeId,
            Name = "Buying Assistant",
            Icon = Icon.Comments,
            Sort = 40,
            HasSubNodes = false,
            NodeAction = NavigateScreenAction.To<BuyingAssistantSettingsScreen>().With(new BuyingAssistantSettingsQuery()),
        };
    }

    public override IEnumerable<NavigationNode> GetSubNodes(NavigationNodePath parentNodePath)
    {
        yield break;
    }
}
