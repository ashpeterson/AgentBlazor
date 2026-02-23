namespace AgentBlazor.Components;

public static class DefaultShippedComponents
{
    public static ComponentCapabilityCatalog CreateCatalog()
    {
        var catalog = new ComponentCapabilityCatalog();
        var builder = new ComponentCapabilityCatalogBuilder(catalog);

        builder.AddComponent(
            "AgentChatWidget",
            "Floating, bottom-right chat launcher and popout panel.",
            new ComponentActionCapability("open_widget", "Open the chat widget."),
            new ComponentActionCapability("close_widget", "Close the chat widget."));

        builder.AddComponent(
            "AgentChatPanel",
            "Docked full-height chat panel for persistent side-by-side workflows.");

        AgentComponentCapabilityProfile.Apply(builder);

        return catalog;
    }
}
