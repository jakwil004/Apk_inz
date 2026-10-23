using System;
using System.Collections.Generic;
using System.Text;
using Usterki.Domain.Enums;

namespace Usterki.Domain.Rules;

public static class TicketStatusRules
{
    public static bool CanChange(
        TicketStatus current,
        TicketStatus target)
    {
        return (current, target) switch
        {
            (TicketStatus.New, TicketStatus.InProgress) => true,
            (TicketStatus.New, TicketStatus.Rejected) => true,

            (TicketStatus.InProgress, TicketStatus.Resolved) => true,
            (TicketStatus.InProgress, TicketStatus.Rejected) => true,

            (TicketStatus.Resolved, TicketStatus.InProgress) => true,
            (TicketStatus.Resolved, TicketStatus.Closed) => true,

            _ => false
        };
    }
}