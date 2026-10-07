using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    // The "ask staff to help" panel (guide to water, leave the queue, escort out) was taken out of the person panel after
    // a playtest: it cluttered the panel and its worth is unproven. The simulation's StaffInterventionCommand stays.

    private string StaffInterventionTargetText(ulong id)
    {
        var job = _session.CaptureStaffInterventions().SingleOrDefault(item => item.GuestId == id &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        if (job is null) return "";
        var name = _session.CapturePreparation()!.People.Single(person => person.AgentId == job.WorkerId).Name;
        return job.Stage == StaffInterventionStage.Travelling ? $"{name} is on the way to help\n" : $"{name} is helping\n";
    }

    private string? ActiveStaffInterventionSummary(ulong workerId)
    {
        var job = _session.CaptureStaffInterventions().SingleOrDefault(item => item.WorkerId == workerId &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        return job is null ? null : $"{job.Action} • {job.Stage}";
    }
}
