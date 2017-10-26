﻿using System;
using Shriek.Events;

namespace Shriek.Samples.Events
{
    public class TodoCreatedEvent : Event<Guid>
    {
        public string Name { get; set; }

        public string Desception { get; set; }
        public DateTime FinishTime { get; internal set; }
    }
}