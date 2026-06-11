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
            .FirstOrDefaultAsync(t => !t.IsDisabled && t.Id == id);
    }

    public async System.Threading.Tasks.Task<List<unigrid.Models.Task>> GetWorkspaceTasksAsync(Guid workspaceId)
    {
        return await _context.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.WorkspaceFiles)
            .Include(t => t.TaskComments)
                .ThenInclude(tc => tc.User)
            .Where(t => !t.IsDisabled && t.WorkspaceId == workspaceId)
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
            .Where(ps => !ps.IsDisabled && ps.TaskId == taskId)
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

    // TaskCategory methods
    public async System.Threading.Tasks.Task<List<TaskCategory>> GetWorkspaceCategoriesAsync(Guid workspaceId)
    {
        return await _context.TaskCategories
            .Where(tc => !tc.IsDisabled && tc.WorkspaceId == workspaceId)
            .OrderBy(tc => tc.Name)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task<TaskCategory?> GetCategoryByIdAsync(Guid id)
    {
        return await _context.TaskCategories
            .FirstOrDefaultAsync(tc => !tc.IsDisabled && tc.Id == id);
    }

    public async System.Threading.Tasks.Task AddCategoryAsync(TaskCategory category)
    {
        await _context.TaskCategories.AddAsync(category);
    }

    public void RemoveCategory(TaskCategory category)
    {
        category.IsDisabled = true;
        _context.TaskCategories.Update(category);
    }

    // KpiTarget methods
    public async System.Threading.Tasks.Task<List<KpiTarget>> GetWorkspaceTargetsAsync(Guid workspaceId)
    {
        return await _context.KpiTargets
            .Include(kt => kt.Category)
            .Include(kt => kt.User)
            .Where(kt => !kt.IsDisabled && kt.WorkspaceId == workspaceId)
            .OrderBy(kt => kt.StartDate)
            .ToListAsync();
    }

    public async System.Threading.Tasks.Task<KpiTarget?> GetTargetByIdAsync(Guid id)
    {
        return await _context.KpiTargets
            .FirstOrDefaultAsync(kt => !kt.IsDisabled && kt.Id == id);
    }

    public async System.Threading.Tasks.Task AddTargetAsync(KpiTarget target)
    {
        await _context.KpiTargets.AddAsync(target);
    }

    public void RemoveTarget(KpiTarget target)
    {
        target.IsDisabled = true;
        _context.KpiTargets.Update(target);
    }

    public async System.Threading.Tasks.Task<List<KpiTarget>> GetUserTargetsForPeriodAsync(Guid workspaceId, Guid userId, string periodType, DateTime startDate, DateTime endDate)
    {
        return await _context.KpiTargets
            .Include(kt => kt.Category)
            .Where(kt => !kt.IsDisabled && 
                         kt.WorkspaceId == workspaceId && 
                         kt.UserId == userId && 
                         kt.PeriodType == periodType && 
                         kt.StartDate >= startDate && 
                         kt.EndDate <= endDate)
            .ToListAsync();
    }
}
