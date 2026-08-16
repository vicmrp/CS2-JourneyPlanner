import React, { useEffect, useMemo } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import "./journey-planner.scss";

const group = "JourneyPlannerNative";

const visible$ = bindValue<boolean>(group, "Visible", false);
const status$ = bindValue<string>(group, "Status", "");
const origin$ = bindValue<string>(group, "Origin", "Not selected");
const destination$ = bindValue<string>(group, "Destination", "Not selected");
const busy$ = bindValue<boolean>(group, "Busy", false);
const routeVisible$ = bindValue<boolean>(group, "RouteVisible", true);
const citizenOrigin$ = bindValue<boolean>(group, "CitizenOrigin", false);
const awaitingDestination$ = bindValue<boolean>(group, "AwaitingDestination", false);
const journeyJson$ = bindValue<string>(group, "JourneyJson", "{\"ready\":false}");

type JourneyLeg = {
  mode: string;
  routeNumber: number;
  from: string;
  to: string;
  distanceMeters: number;
  walkMinutes: number;
  stops: number;
};

type JourneyData = {
  ready: boolean;
  busy?: boolean;
  citizen?: boolean;
  origin?: string;
  destination?: string;
  legs?: JourneyLeg[];
};

const modeIcon = (mode: string) => {
  switch ((mode || "").toLowerCase()) {
    case "walk": return "●";
    case "bus": return "B";
    case "tram": return "T";
    case "metro": return "M";
    case "train": return "R";
    case "ship": return "S";
    case "motorcycle": return "MC";
    case "bike": return "BI";
    case "car": return "C";
    default: return "•";
  }
};

const modeTitle = (leg: JourneyLeg) => {
  const n = leg.routeNumber >= 0 ? ` ${leg.routeNumber}` : "";
  switch ((leg.mode || "").toLowerCase()) {
    case "walk": return "Walk";
    case "bus": return `Bus${n}`;
    case "tram": return `Tram${n}`;
    case "metro": return `Metro${n}`;
    case "train": return `Train${n}`;
    case "ship": return `Ship${n}`;
    case "motorcycle": return "Motorcycle";
    case "bike": return "Bicycle";
    case "car": return "Car";
    default: return leg.mode;
  }
};

const isRealSelection = (value: string) =>
  !!value &&
  value !== "Not selected" &&
  value !== "Click a destination building…" &&
  value !== "Current destination not resolved" &&
  value !== "Destination unavailable";

export const JourneyPlanner = () => {
  const visible = useValue(visible$);
  const status = useValue(status$);
  const origin = useValue(origin$);
  const destination = useValue(destination$);
  const busy = useValue(busy$);
  const routeVisible = useValue(routeVisible$);
  const citizenOrigin = useValue(citizenOrigin$);
  const awaitingDestination = useValue(awaitingDestination$);
  const rawJourney = useValue(journeyJson$);

  const journey = useMemo<JourneyData>(() => {
    try { return JSON.parse(rawJourney || "{\"ready\":false}"); }
    catch { return { ready: false }; }
  }, [rawJourney]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !visible) return;
      console.log("[JP] ESC pressed -> close Journey Planner");
      trigger(group, "Close");
    };

    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [visible]);

  const togglePanel = () => trigger(group, visible ? "Close" : "Open");
  const originSelected = isRealSelection(origin);
  const destinationSelected = isRealSelection(destination);
  const legs = journey.legs || [];
  const walkingMeters = legs
    .filter(x => (x.mode || "").toLowerCase() === "walk")
    .reduce((sum, x) => sum + (x.distanceMeters || 0), 0);
  const walkingMinutes = legs
    .filter(x => (x.mode || "").toLowerCase() === "walk")
    .reduce((sum, x) => sum + (x.walkMinutes || 0), 0);

  const clickOrigin = () => {
    if (originSelected) trigger(group, "OpenOriginInfo");
    else trigger(group, "SelectOrigin");
  };

  const clickDestination = () => {
    if (destinationSelected) trigger(group, "OpenDestinationInfo");
    else trigger(group, "SelectDestination");
  };

  return (
    <>
      <button
        type="button"
        className={`jp-toolbar-button ${visible ? "jp-toolbar-button-active" : ""}`}
        onClick={togglePanel}
        title={visible ? "Close Journey Planner" : "Open Journey Planner"}
      >
        JP
      </button>

      {visible && (
        <div className="jp-panel">
          <header className="jp-header">
            <div>
              <div className="jp-title">Journey Planner</div>
              <div className="jp-subtitle">Click citizens or buildings directly in the city</div>
            </div>
            <button
              type="button"
              className="jp-icon-button"
              onClick={() => trigger(group, "Close")}
              title="Close Journey Planner"
            >
              ×
            </button>
          </header>

          <section className="jp-search">
            <button
              type="button"
              className={`jp-location-row ${originSelected ? "jp-location-selected" : ""}`}
              onClick={clickOrigin}
              title={originSelected ? "Open normal CS2 info panel for start" : "Choose start"}
            >
              <span className="jp-node start-node" />
              <span className="jp-location-copy">
                <small>{citizenOrigin ? "Citizen / Start" : "Start"}</small>
                <b>{originSelected ? origin : "Click a citizen or building in the city"}</b>
                {originSelected && <em>Click this row to open the normal CS2 info panel</em>}
              </span>
              {originSelected && <span className="jp-selected-badge">Selected</span>}
            </button>

            <button
              type="button"
              className={`jp-location-row ${destinationSelected ? "jp-location-selected" : ""} ${awaitingDestination ? "jp-location-required" : ""}`}
              onClick={clickDestination}
              title={destinationSelected ? "Open normal CS2 info panel for destination" : "Choose destination"}
            >
              <span className="jp-node end-node" />
              <span className="jp-location-copy">
                <small>Destination</small>
                <b>
                  {destinationSelected
                    ? destination
                    : awaitingDestination
                      ? "Now click a destination building"
                      : "Click a destination building"}
                </b>
                {awaitingDestination && <em className="jp-required-copy">Waiting for destination B</em>}
                {destinationSelected && <em>Click this row to open the normal CS2 info panel</em>}
              </span>
              {destinationSelected && <span className="jp-selected-badge">Selected</span>}
            </button>
          </section>

          {!busy && !journey.ready && (
            <div className={`jp-help ${awaitingDestination ? "jp-help-required" : ""}`}>
              {awaitingDestination
                ? "Start A is ready. Click another building in the city to set destination B. The journey will calculate automatically."
                : "Click a citizen to show the route CS2 is currently using. Or click a building to begin an A → B journey."}
            </div>
          )}

          {busy && (
            <div className="jp-loading">
              <span className="jp-spinner" />
              Calculating the native CS2 journey…
            </div>
          )}

          {originSelected && destinationSelected && !busy && (
            <button
              type="button"
              className="jp-plan-button"
              onClick={() => trigger(group, "Calculate")}
            >
              Recalculate journey
            </button>
          )}

          {citizenOrigin && (
            <button
              type="button"
              className="jp-follow"
              onClick={() => trigger(group, "RefollowCitizen")}
            >
              Re-follow selected citizen
            </button>
          )}

          {journey.ready && (
            <section className="jp-trip">
              <div className="jp-trip-head">
                <div>
                  <small>Journey to</small>
                  <h2>{journey.destination || destination}</h2>
                </div>
                <div className="jp-route-actions">
                  <button
                    type="button"
                    className="jp-map-toggle"
                    onClick={() => trigger(group, "ToggleRoute")}
                  >
                    {routeVisible ? "Hide route" : "Show route"}
                  </button>

                  <button
                    type="button"
                    className="jp-delete-route"
                    onClick={() => {
                      console.log("[JP] Delete route");
                      trigger(group, "DeleteRoute");
                    }}
                    title="Delete the rendered route but keep Start and Destination"
                  >
                    Delete route
                  </button>
                </div>
              </div>

              <div className="jp-summary">
                <span>{legs.length} legs</span>
                {walkingMeters > 0 && <span>{Math.round(walkingMeters)} m walking</span>}
                {walkingMinutes > 0 && <span>~{walkingMinutes} min walking</span>}
              </div>

              <div className="jp-timeline">
                {legs.map((leg, index) => {
                  const mode = (leg.mode || "").toLowerCase();
                  const transit = !["walk", "car", "bike", "motorcycle"].includes(mode);
                  return (
                    <article className={`jp-leg jp-mode-${mode}`} key={`${index}-${leg.mode}`}>
                      <div className="jp-leg-rail">
                        <span className="jp-mode-icon">{modeIcon(leg.mode)}</span>
                        {index < legs.length - 1 && <span className="jp-rail-line" />}
                      </div>
                      <div className="jp-leg-card">
                        <div className="jp-leg-title-row">
                          <b>{modeTitle(leg)}</b>
                          {mode === "walk" && leg.walkMinutes > 0 && <span>~{leg.walkMinutes} min</span>}
                        </div>
                        <div className="jp-from">{leg.from}</div>
                        <div className="jp-to-label">to</div>
                        <div className="jp-to">{leg.to}</div>
                        <div className="jp-leg-meta">
                          {leg.distanceMeters > 0 && <span>{Math.round(leg.distanceMeters)} m</span>}
                          {transit && leg.stops >= 0 && <span>{leg.stops} {leg.stops === 1 ? "stop" : "stops"}</span>}
                        </div>
                      </div>
                    </article>
                  );
                })}
              </div>
            </section>
          )}

          <footer className="jp-status">{status}</footer>
        </div>
      )}
    </>
  );
};
