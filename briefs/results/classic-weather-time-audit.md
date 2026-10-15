# Shared festival time presentation — consumer audit

15 October 2026, forecast implementation. Runtime timestamp source is Festival.Presentation.FestivalTimePresentation, with its immutable FestivalTimeMapping model and default elapsed placeholder mapping (80 ticks per displayed second). Mapping is presentation-only and has no route into simulation schedules, durations, random streams or persistence.

Timestamp consumers:
- Forecast native screen: header span, quarter-axis ticks, actual stage/arrival/set intervals, exact wet-window detail labels; every label is a native Label with runtime text. Its chart positions use authoritative tick ratios.
- HudKit.FestivalClockText delegates to the shared source; existing top bar, live message stamp, stage cards, band context/NEXT and Steward shift clock consume this adapter.
- Programme/BookingPanel previously duplicated division/formatting and hardcoded 00:00; BookingTime now delegates and gates use Timestamp(0).
- ResultsPaper performance due/played ranges and YourShift start/end/call-event timestamps consume the same adapter.
- Classic death details show the structured casualty timestamp through the adapter. Raw causal evidence's explicit "tick N" timestamps are projected through FestivalEvidenceText before relevant death/disorder/context presentation; authoritative saved narrative text is unchanged. Council hearing's existing human-readable cause/sequence contains no displayed timestamp.

Durations/countdowns remain distinct:
- Day duration after the live clock's slash uses Duration, not Timestamp.
- Stage countdowns use Duration with their existing unpadded-minute style; live set remaining, rain "in"/"dry in" and urgent Steward response countdowns use duration formatting.
- Booking changeovers, treatment/service/ETA/listening seconds, average response durations, performance diagnostic elapsed seconds and hire ability descriptions are duration quantities; their existing numeric meaning is retained. They are not mapped to a festival clock.
- Save creation dates, DateTimeOffset, screenshot filenames, system stopwatch/performance values and ordinal formatting are not festival timestamps and remain outside the mapper.

Focused searches covered FestivalClockText, BookingTime, 80-tick divisions, mm:ss/00:00 and TimeSpan/clock formatting across game C# and the hearing presenter. No forecast-local tick-to-clock conversion was introduced. New temperatures are authoritative data; Celsius is independent of time formatting.

Acceptance: core tests cover negative/zero/sub-second/minute/end boundaries and a temporary alternative mapping, proving Duration is unaffected. Native evidence temporarily switches mapping only for a labelled diagnostic capture, checks the runtime forecast labels and unchanged authoritative hash, then restores the elapsed default. Further native/source review will verify the actual consumers and the approved hierarchy; no real-world opening clock is selected.

Final native/source acceptance: both builder and fresh independent reviewer passed the full243-check weather diagnostic. Temporary mapping changed runtime forecast, gates/programme, live clock, world NEXT, actual Performances ranges and the actual rendered causal newspaper text; durations and authoritative hashes remained unchanged. Final source/UI review inspected16PNG captures and matched42merged pins. The extra overdue-medical cause is explicitly injected only for this diagnostic. Default runtime mapping is restored.
