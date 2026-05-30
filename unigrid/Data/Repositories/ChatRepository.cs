using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly UniGridDbContext _context;

    public ChatRepository(UniGridDbContext context)
    {
        _context = context;
    }

    public async System.Threading.Tasks.Task<ChatRoom?> GetRoomByWorkspaceIdAsync(Guid workspaceId)
    {
        return await _context.ChatRooms.FirstOrDefaultAsync(cr => cr.WorkspaceId == workspaceId);
    }

    public async System.Threading.Tasks.Task<List<ChatMessage>> GetRoomMessagesAsync(Guid roomId)
    {
        return await _context.ChatMessages
            .Include(cm => cm.Sender)
            .Where(cm => cm.RoomId == roomId)
            .OrderBy(cm => cm.SentAt)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task<ChatMessage?> GetMessageByIdAsync(Guid messageId)
    {
        return await _context.ChatMessages
            .Include(cm => cm.Sender)
            .FirstOrDefaultAsync(m => m.Id == messageId);
    }

    public async System.Threading.Tasks.Task AddMessageAsync(ChatMessage message)
    {
        await _context.ChatMessages.AddAsync(message);
    }

    public void UpdateMessage(ChatMessage message)
    {
        _context.ChatMessages.Update(message);
    }
}
