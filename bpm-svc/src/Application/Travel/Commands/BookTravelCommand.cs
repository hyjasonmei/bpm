using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using Bpm.Application.Travel.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Travel.Commands;

public sealed record BookTravelCommand(
    Guid CaseId,
    string AdminUserId,
    string TicketRef,
    string? HotelRef,
    string? BookNote
) : ICommand<TravelCaseDto>;

public sealed class BookTravelCommandValidator : AbstractValidator<BookTravelCommand>
{
    public BookTravelCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.TicketRef).NotEmpty().MaximumLength(64);
    }
}

public sealed class BookTravelCommandHandler(
    IAppDbContext db,
    IClock clock,
    IIdentityProvider identity,
    TravelNotificationEmitter emitter
) : IRequestHandler<BookTravelCommand, TravelCaseDto>
{
    public async Task<TravelCaseDto> Handle(BookTravelCommand request, CancellationToken ct)
    {
        var c = await db.TravelCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("TravelCase", request.CaseId);

        var admin = await identity.FindByIdAsync(request.AdminUserId, ct)
            ?? throw new NotFoundException("Employee", request.AdminUserId);
        if (!admin.Roles.Contains("Admin"))
            throw new ConflictException(
                $"User '{request.AdminUserId}' is not in role 'Admin' (spec.userTasks[task_admin_book].permissions.submitter).");

        c.Book(request.AdminUserId, request.TicketRef, request.HotelRef, request.BookNote, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await emitter.EmitOnCompleteAsync(c, ct);

        return TravelCaseDto.FromDomain(c);
    }
}
