using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using unigrid.Models;

namespace unigrid.Data.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly UniGridDbContext _context;

    public TaskRepository(UniGridDbContext context)
    {
        _context = context;
    }

    public async System.Threading.Tasks.Task<unigrid.Models.Task?> GetByIdAsync(Guid id)
    {
        return await _context.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.WorkspaceFiles)
            .Include(t => t.TaskComments)
                .ThenInclude(tc => tc.User)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async System.Threading.Tasks.Task<List<unigrid.Models.Task>> GetWorkspaceTasksAsync(Guid workspaceId)
    {
        return await _context.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.WorkspaceFiles)
            .Include(t => t.TaskComments)
                .ThenInclude(tc => tc.User)
            .Where(t => t.WorkspaceId == workspaceId)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task AddAsync(unigrid.Models.Task task)
    {
        await _context.Tasks.AddAsync(task);
    }

    public void Update(unigrid.Models.Task task)
    {
        _context.Tasks.Update(task);
    }

    public void Remove(unigrid.Models.Task task)
    {
        _context.Tasks.Remove(task);
    }

    public async System.Threading.Tasks.Task<List<PersonalSchedule>> GetAssociatedSchedulesAsync(Guid taskId)
    {
        return await _context.PersonalSchedules
            .Where(ps => ps.TaskId == taskId)
            .ToListAsync();
    }

    public void RemoveSchedules(IEnumerable<PersonalSchedule> schedules)
    {
        _context.PersonalSchedules.RemoveRange(schedules);
    }

    public async System.Threading.Tasks.Task AddNotificationAsync(Notification notification)
    {
        await _context.Notifications.AddAsync(notification);
    }

    public async System.Threading.Tasks.Task AddCommentAsync(TaskComment comment)
    {
        await _context.TaskComments.AddAsync(comment);
    }
}
