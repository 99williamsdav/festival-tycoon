namespace Festival.Simulation;

public sealed partial class GameSession
{
    public bool IsClassicDeceased(ulong id)=>IsClassicCampaign&&_persons.TryGet(id,out var person)&&person.HealthStage==MedicalStage.Terminal&&
        _lifecycle?.Casualties.Any(c=>c.PersonId==person.Name)==true;
    private static bool SavedClassicDeceased(SessionPersistenceSnapshot s,ulong id)=>SavedClassic(s)&&s.Preparation?.People is{} people&&
        people.FirstOrDefault(p=>p.AgentId==id) is{} p&&s.Lifecycle?.Casualties.Any(c=>c.PersonId==p.Name)==true;
    private void ApplyClassicFatalIncident(FatalIncident incident)
    {
        var victim=incident.Victim;var number=FestivalNumber;var life=_lifecycle!;
        var transaction=$"classic-death:{CampaignId.Value}:{number}:{victim.Name}";
        if(life.CompletedOutcomeTransactionIds.Contains(transaction))return;
        var kind=incident.Kind switch{FatalIncidentKind.Medical=>"medical",FatalIncidentKind.Disorder=>"disorder",_=>"equipment"};
        var casualty=new CasualtySnapshot(life.NextCasualtyId++,life.CurrentAttemptId,victim.Name,victim.Role,incident.Cause,CurrentTick,transaction);
        life.Casualties.Add(casualty);life.CompletedOutcomeTransactionIds.Add(transaction);
        var before=_preparation!.Reputation;var after=Math.Max(0,before-ClassicBalance.DeathReputationPenalty);
        life.CompletedOutcomeTransactionIds.Add($"classic-reputation:{CampaignId.Value}:{number}:{casualty.CasualtyId}");
        _classicHistory=_classicHistory! with{DeathPenalties=[.._classicHistory.DeathPenalties,new(kind,casualty,before,after)]};
        _preparation=_preparation with{Reputation=after};
        RemoveClassicDeceased(victim.Id);
    }
    private void RemoveClassicDeceased(ulong id)
    {
        const string reason="Recorded death; this person's work and claims released";
        InterruptCleanup(id);LeaveImmersionQueue(id,false);LeaveWater(id,reason,false);
        foreach(var fault in OpenFaults.Where(f=>f.WorkerId==id||f.Kind==FacilityFaultKind.StuckInToilet&&f.VictimId==id).ToArray())
        {
            var occupant=fault.Kind==FacilityFaultKind.StuckInToilet&&fault.VictimId==id;
            SetFault(fault with{WorkerId=null,WorkStartedTick=-1,Stage=occupant?FacilityFaultStage.Fixed:fault.Stage,
                ResolvedTick=occupant?CurrentTick:fault.ResolvedTick});
            if(occupant)MedicalEvent("fault:released-after-death",$"{fault.FacilityId}: deceased occupant removed; toilet claim released.");
        }
        if(_cows is{} cows)_cows=cows with{Loose=cows.Loose.Select(c=>c.HerderId==id?c with{HerderId=null,HerdStartedTick=-1,Driving=false,
            Activity=CowActivity.Grazing,Route=[],RouteIndex=0,UntilTick=CurrentTick+400}:c).ToArray()};
        foreach(var toilet in Toilets.Where(t=>t.Queue.Contains(id)||t.OwnerId==id||t.InterruptedOccupantId==id).ToArray())
            SetToilet(toilet with{Queue=toilet.Queue.Where(p=>p!=id).ToArray(),OwnerId=toilet.OwnerId==id?null:toilet.OwnerId,
                InterruptedOccupantId=toilet.InterruptedOccupantId==id?null:toilet.InterruptedOccupantId,
                ServiceTicks=toilet.OwnerId==id?0:toilet.ServiceTicks,DoorOpen=toilet.OwnerId!=id&&toilet.DoorOpen});
        foreach(var queue in _serviceQueues.Values)
        {
            var entity=new EntityId(id);if(!queue.Agents.ContainsKey(entity))continue;
            queue.OrderedMembers.Remove(entity);queue.Agents.Remove(entity);queue.NeedsReassignment=true;
            if(queue.ActiveOwnerId==entity){queue.ActiveOwnerId=null;queue.RemainingServiceTicks=0;}
        }
        foreach(var job in GetMedicResponses().Where(j=>j.PatientId==id||j.WorkerId==id).ToArray())
            SetMedicResponse(job with{Stage=MedicalResponseStage.None,PatientId=null,StartedTick=-1,Description=reason});
        foreach(var job in GetStewardResponses().Where(j=>j.TargetId==id||j.WorkerId==id).ToArray())
            SetStewardResponse(job with{Stage=SecurityResponseStage.Completed,TargetId=null,StartedTick=-1,Incapacitated=false,Description=reason});
        foreach(var job in (_medical?.StaffInterventions??[]).Where(j=>InterventionBusy(j)&&(j.WorkerId==id||j.GuestId==id)).ToArray())
            EndIntervention(job,false,reason);
        if(_equipment is{WorkerId:{} worker} e&&worker==id)
            _equipment=e with{JobStage=MaintenanceStage.None,JobDispatchedTick=-1,RepairStartedTick=-1};
        if(_disorder is{} d)
        {
            foreach(var opponent in PeopleIn(PersonView.Disorder).Where(p=>p.OpponentId==id).ToArray())
                MutatePerson(opponent.Id,p=>{p.ConductStage=DisorderStage.Resolved;p.OpponentId=null;p.Pressure=0;p.Grievance=DisorderGrievance.None;p.CooldownUntilTick=CurrentTick+800;});
            _disorder=_disorder! with{Incidents=d.Incidents.Select(i=>i.HandlingAttempt is{Outcome:FightHandlingOutcome.Handling} h&&
                (h.WorkerId==id||i.InitiatorId==id||i.OpponentId==id)?i with{HandlingAttempt=h with{Outcome=FightHandlingOutcome.Interrupted,EndedTick=CurrentTick}}:i).ToArray()};
        }
        MedicalRelinquishPerformerStage(id);ReleaseWasteAtExit(id);
        MutatePerson(id,p=>{p.Departed=true;p.HealthStage=MedicalStage.Terminal;p.Intent=MedicalIntent.WatchShow;p.Reason=reason;
            p.WaterQueueSlot=null;p.ToiletStage=ToiletVisitStage.None;p.ToiletChoice=null;p.ToiletId=null;p.VendorId=null;p.Order=null;p.Held=null;
            p.ConductStage=DisorderStage.Resolved;p.Pressure=0;p.OpponentId=null;p.Grievance=DisorderGrievance.None;});
        if(_navigationAgents.TryGetValue(new(id),out var nav))
        {nav.Action=AgentNavigationAction.Idle;nav.Destination=null;nav.Route=[];nav.RouteIndex=0;nav.SegmentOriginXMillimetres=nav.XMillimetres;
         nav.SegmentOriginZMillimetres=nav.ZMillimetres;nav.SegmentProgressMicrometres=0;nav.MovementRemainder=0;nav.IntentId=null;}
    }
    private static string? ValidateClassicLifecycle(SessionPersistenceSnapshot s)
    {
        if(s.Preparation is not{} p)return "Classic needs a real festival.";
        if(s.PerformanceRecords?.Any(r=>r is null)==true||s.LivePerformances?.Any(live=>live is not null&&
            (live.Performers is null||live.Performers.Any(p=>p is null)))==true)return "Classic performance record collections are invalid.";
        if(s.Equipment is not{Evidence:not null} equipment||equipment.Evidence.Any(e=>e is null))return "Classic equipment evidence collections are invalid.";
        if(p.People is null||p.People.Any(p=>p is null)||s.Medical is not{Needs:not null,Medics:not null,StaffInterventions:not null,Evidence:not null} medical||
            medical.Needs.Any(n=>n is null)||medical.Medics.Any(j=>j is null)||medical.StaffInterventions.Any(j=>j is null)||medical.Evidence.Any(e=>e is null)||
            s.Disorder is not{Stewards:not null,Evidence:not null,Incidents:not null} disorder||disorder.Stewards.Any(j=>j is null)||disorder.Evidence.Any(e=>e is null)||
            s.Facilities is not{Taps:not null,Toilets:not null} facilities||facilities.Taps.Any(t=>t is null||t.Queue is null||t.Overflow is null)||
            facilities.Toilets.Any(t=>t is null||t.Queue is null)||s.Litter?.Sweeps is null||s.Litter.Sweeps.Any(j=>j is null)||
            s.Faults?.Faults is null||s.Faults.Faults.Any(f=>f is null)||s.Cows?.Loose is null||s.Cows.Loose.Any(c=>c is null))
            return "Classic person, facility or work collections are invalid.";
        var l=s.Lifecycle;
        if(p.Status==PreparationStatus.Preparing)return l is null?null:"Classic preparation has no live lifecycle yet.";
        if(p.Status==PreparationStatus.Failed||p.People is null||p.People.Any(p=>p is null)||s.Medical?.Needs is not{} needs||needs.Any(n=>n is null)||
            s.CampaignPlanning is null||l is null||l.CurrentTierId!="tier-1"||l.TierOrdinal!=1||l.CurrentAttemptId!=1||
            l.NextAttemptId!=2||l.NextHearingId!=1||l.Attempts is not[{AttemptId:1,Status:(int)EditionAttemptStatus.Active,OutcomeTransactionId:null,TierId:"tier-1"}]||
            l.ProtectedPeople is null||l.Casualties is null||l.Hearings is not[]||l.CompletedOutcomeTransactionIds is null||l.FavourBalance is<0 or >2||
            l.ProtectedPeople.Any(p=>p is null)||l.Casualties.Any(c=>c is null)||s.ClassicHistory is not{DeathPenalties:{} changes}||
            changes.Any(c=>c is null)||changes.Length!=l.Casualties.Length||l.NextCasualtyId!=(ulong)changes.Length+1)
            return "Classic lifecycle must continue without Council failure and retain its actual casualties.";
        var protectedPeople=p.People.OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>new PersistedProtectedPerson(p.Name,(int)p.Role)).ToArray();
        if(!l.ProtectedPeople.SequenceEqual(protectedPeople))return "Classic protected people differ from the actual roster.";
        foreach(var (change,index) in changes.Select((c,i)=>(c,i)))
        {
            var c=change.Casualty;var saved=l.Casualties[index];var victim=p.People.FirstOrDefault(p=>p.Name==c?.PersonId);
            if(c is null||saved.CasualtyId!=c.CasualtyId||saved.PersonId!=c.PersonId||saved.AttemptId!=1||saved.Role!=(int)c.Role||
                saved.Cause!=c.Cause||saved.Tick!=c.Tick||saved.TransactionId!=c.TransactionId||victim is null||victim.Role!=c.Role||
                !victim.Admitted||!victim.Departed||c.Tick<p.StartedTick||c.Tick>s.CurrentTick||
                s.Medical?.Needs.FirstOrDefault(n=>n.AgentId==victim.AgentId)?.Stage!=MedicalStage.Terminal)
                return "Classic casualty, reputation record and deceased presence disagree.";
        }
        var terminal=s.Medical?.Needs.Where(n=>n.Stage==MedicalStage.Terminal).Select(n=>n.AgentId).ToArray()??[];
        if(terminal.Length!=changes.Length||terminal.Any(id=>!SavedClassicDeceased(s,id)))return "Classic terminal people lack recorded deaths.";
        var expected=l.Casualties.Select(c=>c.TransactionId).Concat(l.Casualties.Select(c=>$"classic-reputation:{s.CampaignId}:{s.CampaignPlanning!.EditionNumber}:{c.CasualtyId}"))
            .Concat(p.CommunityFavourClaimed?[$"community-water-favour:{s.CampaignId}"]:[]).Order(StringComparer.Ordinal).ToArray();
        if(!l.CompletedOutcomeTransactionIds.SequenceEqual(expected)||expected.Distinct(StringComparer.Ordinal).Count()!=expected.Length)
            return "Classic completed consequence transactions do not match each recorded death exactly once.";
        var dead=terminal.ToHashSet();
        if(s.Medical!.Fatal!=changes.Any(c=>c.Kind=="medical"))return "Classic medical death flag differs from the recorded cause.";
        foreach(var change in changes)
        {
            var c=change.Casualty;
            var evidenceMatches=change.Kind switch{
                "medical"=>s.Medical.Evidence?.Any(e=>e.Id=="medical:death"&&e.Tick==c.Tick&&e.Description==c.Cause)==true,
                "equipment"=>s.Equipment?.Evidence?.Any(e=>e.Id=="equipment:death"&&e.Tick==c.Tick&&e.Description==c.Cause)==true,
                "disorder"=>s.Disorder?.Evidence?.Any(e=>e.Id=="disorder:death"&&e.Tick==c.Tick&&e.Description==c.Cause)==true,
                _=>false};
            if(!evidenceMatches)return "Classic recorded death lacks its causal incident evidence.";
        }
        if(s.Facilities?.Taps.Any(t=>t.Queue.Concat(t.Overflow).Any(dead.Contains)||t.OwnerId is{} id&&dead.Contains(id))==true||
            s.Facilities?.Toilets.Any(t=>t.Queue.Any(dead.Contains)||t.OwnerId is{} id&&dead.Contains(id)||t.InterruptedOccupantId is{} old&&dead.Contains(old))==true||
            s.Immersion?.People.Any(i=>dead.Contains(i.AgentId)&&(i.VendorId is not null||i.Order is not null||i.Held is not null||i.ToiletStage!=ToiletVisitStage.None))==true||
            s.Medical?.Medics.Any(j=>MedicBusy(j)&&(dead.Contains(j.WorkerId)||j.PatientId is{} id&&dead.Contains(id)))==true||
            s.Disorder?.Stewards.Any(j=>StewardBusy(j)&&(dead.Contains(j.WorkerId)||j.TargetId is{} id&&dead.Contains(id)))==true||
            s.Medical?.StaffInterventions.Any(j=>InterventionBusy(j)&&(dead.Contains(j.WorkerId)||dead.Contains(j.GuestId)))==true)
            return "A deceased Classic person retains a queue, service or staff claim.";
        if(s.NavigationAgents?.Any(n=>dead.Contains(n.Id)&&(n.Action!=(int)AgentNavigationAction.Idle||n.DestinationX is not null||n.DestinationZ is not null||
                n.Route.Length!=0||n.IntentId is not null))==true||
            s.ServiceQueues?.Any(q=>q.OrderedMembers.Any(dead.Contains)||q.Agents.Any(a=>dead.Contains(a.AgentId))||q.ActiveOwnerId is{} id&&dead.Contains(id))==true||
            s.LivePerformances?.Any(live=>live is not null&&live.Performers.Any(p=>dead.Contains(p.AgentId)&&(p.OnStage||p.InstrumentAttached)))==true||
            s.Litter?.Sweeps.Any(j=>dead.Contains(j.WorkerId)&&j.Remaining>0)==true||
            s.Faults?.Faults.Any(f=>f.Stage!=FacilityFaultStage.Fixed&&(f.WorkerId is{} id&&dead.Contains(id)||f.Kind==FacilityFaultKind.StuckInToilet&&dead.Contains(f.VictimId)))==true||
            s.Cows?.Loose.Any(c=>c.HerderId is{} id&&dead.Contains(id))==true||
            s.Equipment is{WorkerId:{} worker,JobStage:not MaintenanceStage.None}&&dead.Contains(worker))
            return "A deceased Classic person retains navigation, performance or work ownership.";
        return null;
    }
}
