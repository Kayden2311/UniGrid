using System;
using System.Threading.Tasks;

namespace unigrid.Services
{
    public interface INotificationService
    {
        System.Threading.Tasks.Task CreateAndSendNotificationAsync(Guid userId, string message, string type, string? link, Guid? relatedId);
    }
}
