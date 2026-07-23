using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure;

namespace Api.Features.Activities;

public static class ActivitiesEndpoints
{
    // Matches @<uuid> patterns in note text; each capture group holds the UUID string.
    private static readonly Regex MentionPattern =
        new(@"@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
            RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapActivitiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/activities");
        group.MapPost("/", CreateActivity);
        return app;
    }

    private static async Task<IResult> CreateActivity(
        CreateActivityRequest req,
        CrmDbContext db,
        INotificationDispatcher dispatcher)
    {
        Activity activity;
        try
        {
            activity = Activity.Create(
                req.Type,
                req.Note,
                req.OccurredAt,
                req.ContactId,
                req.CompanyId,
                req.DealId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        var mentionedUserIds = ParseMentions(req.Note);
        if (mentionedUserIds.Count > 0)
            await dispatcher.ActivityMentionAsync(activity, mentionedUserIds);

        var response = new ActivityResponse(
            activity.Id,
            activity.Type.ToString(),
            activity.Note,
            activity.OccurredAt,
            activity.ContactId,
            activity.CompanyId,
            activity.DealId);

        return Results.Created($"/activities/{activity.Id}", response);
    }

    private static IReadOnlyList<Guid> ParseMentions(string? note)
    {
        if (string.IsNullOrEmpty(note))
            return Array.Empty<Guid>();

        return MentionPattern.Matches(note)
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();
    }
}
