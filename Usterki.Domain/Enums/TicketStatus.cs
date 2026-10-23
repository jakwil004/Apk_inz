using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Enums;

public enum TicketStatus
{
    New = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4,
    Rejected = 5
}