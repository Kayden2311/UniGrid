namespace unigrid.Models.AI
{
    public class AssistantRequest
    {
        public string Message { get; set; } = string.Empty;

        public List<AssistantMessage> History { get; set; }
            = new();
    }
}