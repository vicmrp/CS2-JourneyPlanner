import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import "./journey-planner.scss";

type SelectionMode = "none" | "start" | "destination";

const selectionMode$ = bindValue<SelectionMode>(
  "JourneyPlanner",
  "SelectionMode",
  "none"
);

const hasStart$ = bindValue<boolean>(
  "JourneyPlanner",
  "HasStart",
  false
);

const hasDestination$ = bindValue<boolean>(
  "JourneyPlanner",
  "HasDestination",
  false
);

export const JourneyPlanner = () => {
    const selectionMode = useValue(selectionMode$);
    const hasStart = useValue(hasStart$);
    const hasDestination = useValue(hasDestination$);

  const selectStart = () => {
    trigger("JourneyPlanner", "SelectStart");
  };

  const selectDestination = () => {
    trigger("JourneyPlanner", "SelectDestination");
  };

  const calculateRoute = () => {
    trigger("JourneyPlanner", "CalculateRoute");
  };

  const clearRoute = () => {
    trigger("JourneyPlanner", "ClearRoute");
  };

  return (
    <div className="journey-planner">
      <div className="journey-planner__header">
        Journey Planner
      </div>

      <div className="journey-planner__body">
        <div className="journey-point">
          <div className="journey-point__marker journey-point__marker--start" />

          <div className="journey-point__content">
            <div className="journey-point__label">Start</div>

            <div className="journey-point__status">
              {hasStart ? "Point selected" : "No point selected"}
            </div>
          </div>

          <button
            className={
              selectionMode === "start"
                ? "journey-button journey-button--active"
                : "journey-button"
            }
            onClick={selectStart}
          >
            {selectionMode === "start" ? "Click on map…" : "Select"}
          </button>
        </div>

        <div className="journey-point">
          <div className="journey-point__marker journey-point__marker--destination" />

          <div className="journey-point__content">
            <div className="journey-point__label">Destination</div>

            <div className="journey-point__status">
              {hasDestination ? "Point selected" : "No point selected"}
            </div>
          </div>

          <button
            className={
              selectionMode === "destination"
                ? "journey-button journey-button--active"
                : "journey-button"
            }
            onClick={selectDestination}
          >
            {selectionMode === "destination"
              ? "Click on map…"
              : "Select"}
          </button>
        </div>

        <div className="journey-planner__actions">
          <button
            className="journey-button"
            onClick={clearRoute}
            disabled={!hasStart && !hasDestination}
          >
            Clear
          </button>

          <button
            className="journey-button journey-button--primary"
            onClick={calculateRoute}
            disabled={!hasStart || !hasDestination}
          >
            Calculate walking route
          </button>
        </div>
      </div>
    </div>
  );
};