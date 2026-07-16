namespace unigrid.Services
{
    public interface IAIAssistantService
    {
        Task<unigrid.Models.AI.AssistantResponse> AskAsync(
            Guid userId,
            string message,
            List<unigrid.Models.AI.AssistantMessage>? history = null);
    }
}
