using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace OhMyHarness.Core;

public sealed class PendingInput
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int ProviderId { get; set; }
    public string Mode { get; set; } = "queued";
    public string Text { get; set; } = "";
    public string ImagesJson { get; set; } = "[]";
    public List<Attachment> Images() => JsonSerializer.Deserialize<List<Attachment>>(ImagesJson) ?? [];
}
public static class ConversationInbox
{
    public static async Task UpdateAsync(string database, int chatId, int id, string expectedText, string text, bool steer = false, int? providerId = null, CancellationToken ct = default)
    {
        await using var db = new HarnessDb(database);
        var input = await db.PendingInputs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ChatId == chatId, ct)
            ?? throw new InvalidOperationException("Ce message a déjà été envoyé ou supprimé.");
        if (string.IsNullOrWhiteSpace(text) && input.Images().Count == 0) throw new ArgumentException("Message requis.");
        var updated = await db.PendingInputs.Where(x => x.Id == id && x.ChatId == chatId && x.Text == expectedText && x.Mode == input.Mode)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Text, text).SetProperty(x => x.Mode, steer ? "steering" : input.Mode)
                .SetProperty(x => x.ProviderId, providerId ?? input.ProviderId), ct);
        if (updated != 1) throw new InvalidOperationException("Ce message a été modifié, envoyé ou supprimé. Actualisez la file.");
    }
    public static async Task<PendingInput> AddAsync(string database, int chatId, int providerId, string text, IEnumerable<Attachment> images, string mode, CancellationToken ct=default)
    {
        if(mode is not ("queued" or "steering")) throw new ArgumentException("Mode d'envoi invalide.");
        var attachments=images.Select(x=>new Attachment{Name=x.Name,Mime=x.Mime,Data=x.Data}).ToList();
        if(string.IsNullOrWhiteSpace(text)&&attachments.Count==0) throw new ArgumentException("Message requis.");
        await using var db=new HarnessDb(database);
        var input=new PendingInput{ChatId=chatId,ProviderId=providerId,Text=text,Mode=mode,ImagesJson=JsonSerializer.Serialize(attachments)};
        db.PendingInputs.Add(input);await db.SaveChangesAsync(ct);return input;
    }
    public static async Task<List<Message>> ApplySteeringAsync(ConversationSession run, CancellationToken ct)
    {
        await using var transaction=await run.Db.Database.BeginTransactionAsync(ct);
        var inputs=await run.Db.PendingInputs.Where(x=>x.ChatId==run.Chat.Id && x.Mode=="steering").OrderBy(x=>x.Id).ToListAsync(ct);
        var messages=inputs.Select(x=>new Message{ChatId=run.Chat.Id,Content=x.Text,Attachments=x.Images()}).ToList();
        // Called only between complete assistant/tool groups, never during an in-flight request.
        run.Db.Messages.AddRange(messages);run.Db.PendingInputs.RemoveRange(inputs);
        if(inputs.Count>0)await run.Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return messages;
    }
    public static async Task SubmitAsync(ConversationSession run,Message user,CancellationToken ct)
    {
        await using var transaction=await run.Db.Database.BeginTransactionAsync(ct);
        await ConsumeAsync(run,ct);run.Db.Messages.Add(user);
        await run.Db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
    }
    public static async Task ConsumeAsync(ConversationSession run, CancellationToken ct)
    {
        if(run.PendingInputId==0)return;
        var input=await run.Db.PendingInputs.SingleOrDefaultAsync(x=>x.Id==run.PendingInputId && x.ChatId==run.Chat.Id,ct);
        if(input==null)throw new InvalidOperationException("Message en attente introuvable.");
        if(input.Text.Trim() != run.Prompt.Trim() || input.ProviderId != run.SelectedProviderId)
            throw new InvalidOperationException("Le message a été modifié avant son envoi. Il reste dans la file : reprenez-la pour envoyer la nouvelle version.");
        run.Db.PendingInputs.Remove(input); // Saved atomically with the first user message.
    }
}
