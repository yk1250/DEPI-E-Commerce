using Clinic.Domain.Entities;
using Clinic.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clinic.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.SenderUserId);
        builder.HasOne<Attachment>().WithMany().HasForeignKey(m => m.AttachmentId).IsRequired(false);
    }
}
