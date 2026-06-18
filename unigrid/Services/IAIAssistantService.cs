namespace unigrid.Services
{
    public interface IAIAssistantService
    {
        Task<string> AskAsync(
            Guid userId,
            string message,
            List<unigrid.Models.AI.AssistantMessage>? history = null);
    }
}
