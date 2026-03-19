namespace AgentBlazor.DefaultAgent;

[Obsolete("AgentBlazor.DefaultAgent is a legacy compatibility surface. Prefer explicit agent registration and runtime-adapter-backed capability projection.", false)]
public interface IDefaultComponentAwareAgentDescriptorProvider
{
    DefaultComponentAwareAgentDescriptor GetDescriptor();
}
