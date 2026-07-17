using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Notifications;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications");

        group.MapGet("/", ListNotifications);
        group.MapPatch("/{id:guid}/read", MarkRead);
        group.MapPost("/read-all", MarkAllRead);

        return app;
    }

    private static async Task<IResult> ListNotifications(
        Guid? userId,
        bool? isRead,
        int limit,
        CrmDbContext db)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required."] },
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        var query = db.Notifications
            .Where(n => n.RecipientUserId == userId!.Value);

        if (isRead.HasValue)
            query = query.Where(n => n.IsRead == isRead.Value);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationItemResponse(
                n.Id,
                n.Trigger.ToString(),
                n.Title,
                n.Body,
                n.RelatedEntityId,
                n.RelatedEntityType.HasValue ? n.RelatedEntityType.Value.ToString() : null,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync();

        var unreadCount = await db.Notifications
            .CountAsync(n => n.RecipientUserId == userId!.Value && !n.IsRead);

        return Results.Ok(new NotificationsListResponse(items.ToArray(), unreadCount));
    }

    private static async Task<IResult> MarkRead(Guid id, CrmDbContext db)
    {
        var notification = await db.Notifications.FindAsync(id);
        if (notification is null)
            return Results.NotFound();

        notification.MarkRead();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllRead(Guid? userId, CrmDbContext db)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["userId"] = ["userId is required."] },
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var unread = await db.Notifications
            .Where(n => n.RecipientUserId == userId.Value && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
            n.MarkRead();

        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
