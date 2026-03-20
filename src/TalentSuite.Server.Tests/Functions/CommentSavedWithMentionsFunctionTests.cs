using Microsoft.Extensions.Logging.Abstractions;
using TalentSuite.Functions.CommentEmail;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging.Events;

namespace TalentSuite.Server.Tests.Functions;

public class CommentSavedWithMentionsFunctionTests
{
    [Test]
    public async Task Run_WithMentionedUsers_SendsEmailToEachUniqueRecipient()
    {
        var sender = new FakeCommentMentionEmailSender();
        var sut = new CommentSavedWithMentionsFunction(NullLogger<CommentSavedWithMentionsFunction>.Instance, sender);

        var payload = new CommentSavedWithMentionsEvent
        {
            BidId = "bid-1",
            QuestionId = "q-1",
            CommentId = "c-1",
            Tab = "drafts",
            Comment = "Please update this section.",
            QuestionLink = "https://localhost:5173/bids/manage/bid-1?questionId=q-1&tab=drafts&commentId=c-1",
            MentionedUsers =
            [
                new CommentMentionedUser { FullName = "User One", Email = "user1@test.local" },
                new CommentMentionedUser { FullName = "User Two", Email = "user2@test.local" },
                new CommentMentionedUser { FullName = "User One Duplicate", Email = "USER1@test.local" }
            ]
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, SerialiserOptions.JsonOptions);

        await sut.Run(json, CancellationToken.None);

        Assert.That(sender.Sends, Has.Count.EqualTo(2));
        Assert.That(sender.Sends.Select(x => x.MentionedUser.Email), Is.EquivalentTo(["user1@test.local", "user2@test.local"]));
    }

    [Test]
    public async Task Run_WithoutMentionedUsers_DoesNotSendEmails()
    {
        var sender = new FakeCommentMentionEmailSender();
        var sut = new CommentSavedWithMentionsFunction(NullLogger<CommentSavedWithMentionsFunction>.Instance, sender);

        var payload = new CommentSavedWithMentionsEvent
        {
            BidId = "bid-1",
            QuestionId = "q-1",
            MentionedUsers = []
        };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, SerialiserOptions.JsonOptions);

        await sut.Run(json, CancellationToken.None);

        Assert.That(sender.Sends, Is.Empty);
    }

    [Test]
    public void Run_InvalidJson_ThrowsJsonException()
    {
        var sender = new FakeCommentMentionEmailSender();
        var sut = new CommentSavedWithMentionsFunction(NullLogger<CommentSavedWithMentionsFunction>.Instance, sender);

        Assert.ThrowsAsync<System.Text.Json.JsonException>(async () => await sut.Run("{ invalid", CancellationToken.None));
        Assert.That(sender.Sends, Is.Empty);
    }

    private sealed class FakeCommentMentionEmailSender : ICommentMentionEmailSender
    {
        public List<SendCall> Sends { get; } = new();

        public Task SendCommentMentionAsync(
            CommentSavedWithMentionsEvent @event,
            CommentMentionedUser mentionedUser,
            CancellationToken ct = default)
        {
            Sends.Add(new SendCall(@event, mentionedUser));
            return Task.CompletedTask;
        }
    }

    private sealed record SendCall(
        CommentSavedWithMentionsEvent Event,
        CommentMentionedUser MentionedUser);
}
