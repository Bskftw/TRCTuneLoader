// A race's lobby: the drivers, the host's buttons and the next step, and console players to add.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawRaceLobby() {
			RaceSession r = race_;
			double now = BattleNow;
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label(r.Settings.Name.Length > 0 ? r.Settings.Name : $"{r.HostName}'s race", Theme.Heading);
			string laps = r.Course?.IsCircuit == true ? $" · {r.Settings.Laps} lap{(r.Settings.Laps == 1 ? "" : "s")}{(r.Settings.Elimination ? ", elimination" : "")}" : "";
			if (r.IsGame) {
				GUILayout.Label($"{RoomModeNames[(int)r.Settings.Mode]} · anywhere on the map{(r.Settings.Mode != RoomMode.Convoy ? $" · {r.Settings.RoundMinutes} min" : "")}", Theme.Small);
				GUILayout.Label(RoomModeRules[(int)r.Settings.Mode], Theme.Small);
			} else {
				string jump = r.Settings.Jump == JumpRule.Disqualify ? "disqualifies" : r.Settings.Jump == JumpRule.Recall ? "recalls the start" : r.Settings.Jump == JumpRule.None || r.Settings.NoPenalties ? "no penalty" : $"+{r.Settings.JumpPenaltySeconds:0} s";
				GUILayout.Label($"{r.Course?.Name ?? r.CourseCode}{laps} · {RuleText(r.Settings.Open, r.Settings.Regulation, r.Settings.Fun)}{(r.Settings.SpeedTrapScoring ? " · speed trap race" : "")} · grid {GridOrders[(int)Math.Min((int)r.Settings.Order, 3)].ToLowerInvariant()}, " +
					$"{(r.Settings.PoleLeft ? "pole left" : "pole right")} · jump start {jump}{(r.Settings.NoPenalties ? " · no penalties" : "")}{(r.Settings.HostDrives ? "" : " · the host isn't driving")}", Theme.Small);
				if (r.Settings.Regulation != null) {
					RegulationResult mine = r.Settings.Regulation.Check(MyCarFacts());
					GUILayout.Label($"{r.Settings.Regulation.Name}: {r.Settings.Regulation.Summary()}", Theme.Small);
					if (r.MyDriver?.Racer == true || !r.IsHost) {
						GUILayout.Label(mine.Passes ? "Your car meets the regulation." : $"Your car doesn't meet it: {string.Join("; ", mine.Broken.Take(3))}", mine.Passes ? Theme.PillGood : Theme.PillWarn);
					}
					if (RegulationByCode(r.Settings.Regulation.Code) == null && NavButton("Save this regulation", Theme.Button)) {
						SaveRegulation(r.Settings.Regulation.Copy(), $"Saved {r.Settings.Regulation.Name}.");
					}
				}
			}
			GUILayout.Label(r.Message, Theme.Body);
			if (r.RoundTrip > 0) {
				GUILayout.Label($"Connection to the host: {r.RoundTrip * 1000:0} ms there and back", Theme.Small);
			}
			GUILayout.EndVertical();

			if (r.Lines.Count > 0) {
				GUILayout.BeginVertical(Theme.Card);
				foreach (string line in r.Lines) {
					GUILayout.Label(line, line == r.Lines[0] ? Theme.Body : Theme.Small);
				}
				GUILayout.EndVertical();
			}

			if (r.Results != null) {
				GUILayout.Label("Results", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Card);
				double winner = r.Results.FirstOrDefault()?.State == RacerState.Finished ? r.Results[0].Time : 0;
				foreach (RaceResultRow row in r.Results) {
					string gap = row.State == RacerState.Finished && row.Position > 1 && winner > 0 ? $" <color={Theme.MutedHex}>{TimeAttackRun.FormatGap(row.Time - winner)}</color>" : "";
					string lap = row.BestLap > 0 ? $" <color={Theme.MutedHex}>· best lap {TimeAttackRun.FormatTime(row.BestLap)}</color>" : "";
					GUILayout.BeginHorizontal();
					string incidents = row.Incidents > 0 ? $" <color={Theme.WarnHex}>· {row.Incidents} incident(s)</color>" : "";
					GUILayout.Label($"<b>P{row.Position}</b>  {row.Name}  {RowText(row)}{(r.IsGame ? "" : gap + lap)}{incidents}", row.Id == MySteamId ? Theme.Body : Theme.Small);
					GUILayout.FlexibleSpace();
					// The host as race director: penalties after the race, and everyone sees the new results.
					if (r.IsHost && !r.IsGame && !r.Settings.NoPenalties) {
						if (NavButton("+5 s", Theme.Button)) {
							r.Penalize(row.Id, 5, false, BattleNow);
							TrcLog.Info($"Race {r.Id}: +5 s for {row.Name}.");
						}
						if (NavButton("+10 s", Theme.Button)) {
							r.Penalize(row.Id, 10, false, BattleNow);
							TrcLog.Info($"Race {r.Id}: +10 s for {row.Name}.");
						}
						if (NavButton("DQ", Theme.Button)) {
							r.Penalize(row.Id, 0, true, BattleNow);
							TrcLog.Info($"Race {r.Id}: {row.Name} disqualified by the host.");
						}
						if ((row.Penalty > 0 || row.State == RacerState.Disqualified) && NavButton("Clear", Theme.Button)) {
							r.Penalize(row.Id, 0, false, BattleNow);
						}
					}
					GUILayout.EndHorizontal();
				}
				GUILayout.EndVertical();
			}

			GUILayout.Label($"Drivers ({r.Drivers.Count}/{MaxDriversText(r.Settings.MaxPlayers)})", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			bool racing = r.Phase == RacePhase.Gridding || r.Phase == RacePhase.Countdown || r.Phase == RacePhase.Running;
			List<RaceStanding> standings = racing ? r.Standings(now) : null;
			foreach (RaceDriver d in racing ? standings.Select(s => s.Driver).Concat(r.Drivers.Where(x => !x.Racer)) : r.Drivers.OrderBy(x => x.Slot < 0 ? 99 : x.Slot)) {
				var parts = new List<string>();
				if (d.Slot >= 0 && r.Phase == RacePhase.Gridding) {
					parts.Add($"grid {d.Slot + 1}");
				}
				if (racing && d.Racer) {
					parts.Add($"P{standings.First(s => s.Driver == d).Position}");
				}
				if (!d.Racer) {
					parts.Add(d.Id == r.HostId && !r.Settings.HostDrives ? "running the race" : "watching");
				}
				if (d.Console) {
					parts.Add($"{(d.Platform.Length > 0 ? d.Platform : "console")}, timed by the host");
				}
				if (r.Settings.Regulation != null && d.Racer && !d.Console && d.Id != MySteamId && r.Phase == RacePhase.Gridding) {
					parts.Add(!d.SaidRuleOk ? $"<color={Theme.WarnHex}>needs 0.16</color>" : d.RuleOk ? "meets the regulation" : $"<color={Theme.WarnHex}>doesn't meet the regulation</color>");
				}
				if (d.Car.Length > 0) {
					parts.Add($"{d.Car}{(d.TuneSet.Length > 0 && r.Settings.IsTrcSpec ? $", {d.TuneSet}" : "")}{(d.Spec || !r.Settings.IsTrcSpec ? "" : " (not spec)")}");
				}
				if (r.Phase == RacePhase.Gridding && d.Racer) {
					parts.Add(d.Ready ? $"<color={Theme.GoodHex}>ready</color>" : "lining up");
				}
				string rating = RatingOf(d.Id, r.Course?.Kind ?? CourseKind.Touge);
				if (rating.Length > 0) {
					parts.Add(rating);
				}
				if (d.BestQualifying > 0) {
					parts.Add($"qualified {TimeAttackRun.FormatTime(d.BestQualifying)}");
				}
				if (d.State == RacerState.Finished && racing) {
					parts.Add($"finished {TimeAttackRun.FormatTime(d.Time)}");
				} else if (d.State != RacerState.Racing && racing) {
					parts.Add($"<color={Theme.BadHex}>{d.State.ToString().ToLowerInvariant()}</color>");
				}
				if (r.IsGame && d.Role != RoomRole.None) {
					parts.Insert(0, d.Role.ToString().ToLowerInvariant());
				}
				if (r.IsGame && racing) {
					parts.Add($"{d.Points} pts{(d.Out ? " (out)" : "")}");
				}
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{d.Name}</b>{(d.Id == r.HostId ? " (host)" : "")}  <color={Theme.MutedHex}>{string.Join(" · ", parts)}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (r.IsHost && d.Id != MySteamId && NavButton("Remove", Theme.Button)) {
					r.Kick(d.Id, now);
				}
				GUILayout.EndHorizontal();
				// Race control: the host sees each driver's incidents as their mod reported them.
				if (r.IsHost && d.IncidentLog.Count > 0) {
					GUILayout.Label($"<color={Theme.WarnHex}>    {string.Join("  ·  ", d.IncidentLog.Skip(Math.Max(0, d.IncidentLog.Count - 3)))}</color>", Theme.Small);
				}
			}
			GUILayout.EndVertical();
			if (r.IsHost && !r.IsGame) {
				DrawConsolePlayersToAdd(r);
			}

			GUILayout.BeginHorizontal();
			if (r.Phase == RacePhase.Gridding && r.MySpot().HasValue && NavButton("Teleport to my spot", Theme.ButtonPrimary)) {
				TeleportToRaceSpot();
			}
			if (r.Phase == RacePhase.Qualifying && NavButton("Teleport to start", Theme.ButtonPrimary)) {
				TeleportToQualifyingStart();
			}
			if (r.IsHost) {
				DrawHostButtons(r, now);
			}
			GUILayout.FlexibleSpace();
			if (NavButton(r.IsHost ? "Close race" : "Leave race", Theme.Button)) {
				LeaveRace();
			}
			GUILayout.EndHorizontal();
		}

		private void DrawHostButtons(RaceSession r, double now) {
			switch (r.Phase) {
				case RacePhase.Lobby:
				case RacePhase.Finished:
					if (hostTournament_ > 0 && tournament_ == null && !r.IsGame) {
						if (r.Settings.Order == GridOrder.Qualifying && NavButton("Start qualifying", Theme.Button)) {
							r.StartQualifying(now);
						}
						if (r.Drivers.Count >= 2 && NavButton("Start the tournament", Theme.ButtonPrimary)) {
							StartTournament();
							if (tournament_.Settings.Kind != TournamentKind.Knockout) {
								NextTournamentStep();
							}
						}
					} else if (tournament_ != null) {
						if (!tournament_.Done && NavButton(NextStepLabel(), Theme.ButtonPrimary)) {
							NextTournamentStep();
						}
					} else {
						if (r.Settings.Order == GridOrder.Qualifying && NavButton("Start qualifying", Theme.Button)) {
							r.StartQualifying(now);
						}
						if (NavButton(r.Phase == RacePhase.Finished ? "Run it back" : "Start race", Theme.ButtonPrimary)) {
							r.StartRace(now);
						}
					}
					if (r.Phase == RacePhase.Finished && NavButton("Back to lobby", Theme.Button)) {
						r.BackToLobby();
					}
					break;
				case RacePhase.Qualifying:
					if (NavButton("End qualifying", Theme.Button)) {
						r.EndQualifying(now);
					}
					break;
				case RacePhase.Gridding:
					if (NavButton("Go now", Theme.Button)) {
						r.GoNow(now);
					}
					if (NavButton("Call it off", Theme.Button)) {
						r.EndRace(now, 0);
					}
					break;
				case RacePhase.Countdown:
				case RacePhase.Running:
					if (r.EndAt < 0 && NavButton("End in 60 s", Theme.Button)) {
						r.EndRace(now, 60);
					}
					if (NavButton("End now", Theme.Button)) {
						r.EndRace(now, 0);
					}
					break;
			}
		}

		private string NextStepLabel() {
			switch (tournament_.Settings.Kind) {
				case TournamentKind.Points:
					return $"Start round {tournament_.RoundIndex + 1}";
				case TournamentKind.Heats:
					return tournament_.InFinal ? "Start the final" : $"Start heat {tournament_.HeatIndex + 1}";
				case TournamentKind.Knockout:
					return race_.MatchId != 0 ? "Call the battle again" : "Call the next battle";
				default:
					return "Start the shootout";
			}
		}

		/// <summary>The host's list of console players in the room who aren't in the race yet, to add.</summary>
		private void DrawConsolePlayersToAdd(RaceSession r) {
			RefreshPlayersIfDue();
			List<PlayerRow> consoles = players_.Where(p => p.Player.IsConsole && !r.Drivers.Any(d => d.Id == RaceSession.ConsoleId(p.Player.Key))).ToList();
			GUILayout.Label("Console players", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Players on PlayStation, Xbox or Switch can't run the mod: add them and your mod times them from where their car is. " +
				"They see the start on your headlights (on, off at 3, 2 and 1, off at GO), so they need CarX's synced headlights on (the default). " +
				"Stay in the room until the race is over.", Theme.Small);
			if (consoles.Count == 0) {
				GUILayout.Label("No console players in the room to add.", Theme.Small);
			}
			foreach (PlayerRow c in consoles) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{c.Player.Name}</b>  <color={Theme.MutedHex}>{c.Player.Platform} · {c.Player.CarName} · {c.SetupText}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && r.Drivers.Count < r.Settings.MaxPlayers;
				if (NavButton("Add", Theme.ButtonPrimary)) {
					AddConsoleRacer(r, c.Player);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}
	}
}
