import React from "react";
import {
  bindValue,
  trigger,
  useValue,
} from "cs2/api";

import "./journey-planner.scss";

const bindingGroup = "JourneyPlanner";

const selectionMode$ = bindValue<string>(
  bindingGroup,
  "SelectionMode",
  "None",
);

const hasStart$ = bindValue<boolean>(
  bindingGroup,
  "HasStart",
  false,
);

const hasDestination$ = bindValue<boolean>(
  bindingGroup,
  "HasDestination",
  false,
);

const status$ = bindValue<string>(
  bindingGroup,
  "Status",
  "Select a starting point.",
);

const startPosition$ = bindValue<string>(
  bindingGroup,
  "StartPosition",
  "",
);

const destinationPosition$ = bindValue<string>(
  bindingGroup,
  "DestinationPosition",
  "",
);

const startEntityType$ = bindValue<string>(
  bindingGroup,
  "StartEntityType",
  "",
);

const destinationEntityType$ = bindValue<string>(
  bindingGroup,
  "DestinationEntityType",
  "",
);

const startRoadName$ = bindValue<string>(
  bindingGroup,
  "StartRoadName",
  "",
);

const destinationRoadName$ = bindValue<string>(
  bindingGroup,
  "DestinationRoadName",
  "",
);

function selectStart(): void {
  trigger(bindingGroup, "SelectStart");
}

function selectDestination(): void {
  trigger(bindingGroup, "SelectDestination");
}

function clearStart(): void {
  trigger(bindingGroup, "ClearStart");
}

function clearDestination(): void {
  trigger(bindingGroup, "ClearDestination");
}

function clearAll(): void {
  trigger(bindingGroup, "ClearAll");
}

function calculateRoute(): void {
  trigger(bindingGroup, "CalculateRoute");
}

interface SelectionCardProps {
  title: string;
  hasSelection: boolean;
  roadName: string;
  position: string;
  entity: string;
  selecting: boolean;
  onSelect: () => void;
  onClear: () => void;
}

function SelectionCard({
  title,
  hasSelection,
  roadName,
  position,
  entity,
  selecting,
  onSelect,
  onClear,
}: SelectionCardProps): JSX.Element {
  return (
    <section className="journey-planner__selection">
      <div className="journey-planner__selection-header">
        <h3>{title}</h3>

        {hasSelection && (
          <button
            className="journey-planner__small-button"
            type="button"
            onClick={onClear}
          >
            Clear
          </button>
        )}
      </div>

      {hasSelection ? (
        <div className="journey-planner__selection-content">
          <div className="journey-planner__road-name">
            {roadName || "Unnamed road"}
          </div>

          {position && (
            <div className="journey-planner__detail">
              {position}
            </div>
          )}

          {entity && (
            <div className="journey-planner__detail">
              {entity}
            </div>
          )}

          <button
            className="journey-planner__button"
            type="button"
            onClick={onSelect}
          >
            {selecting ? "Selecting…" : "Change"}
          </button>
        </div>
      ) : (
        <button
          className="journey-planner__button"
          type="button"
          onClick={onSelect}
        >
          {selecting
            ? "Click a road…"
            : `Select ${title}`}
        </button>
      )}
    </section>
  );
}

export const JourneyPlanner = (): JSX.Element => {
  const selectionMode =
    useValue(selectionMode$);

  const hasStart =
    useValue(hasStart$);

  const hasDestination =
    useValue(hasDestination$);

  const status =
    useValue(status$);

  const startPosition =
    useValue(startPosition$);

  const destinationPosition =
    useValue(destinationPosition$);

  const startEntityType =
    useValue(startEntityType$);

  const destinationEntityType =
    useValue(destinationEntityType$);

  const startRoadName =
    useValue(startRoadName$);

  const destinationRoadName =
    useValue(destinationRoadName$);

  const selectingStart =
    selectionMode === "Start";

  const selectingDestination =
    selectionMode === "Destination";

  const canCalculate =
    hasStart && hasDestination;

  return (
    <div className="journey-planner">
      <header className="journey-planner__header">
        <div>
          <h2>Journey Planner</h2>

          <div className="journey-planner__version">
            v0.1e · Road Names
          </div>
        </div>
      </header>

      <div className="journey-planner__body">
        <SelectionCard
          title="Start"
          hasSelection={hasStart}
          roadName={startRoadName}
          position={startPosition}
          entity={startEntityType}
          selecting={selectingStart}
          onSelect={selectStart}
          onClear={clearStart}
        />

        <SelectionCard
          title="Destination"
          hasSelection={hasDestination}
          roadName={destinationRoadName}
          position={destinationPosition}
          entity={destinationEntityType}
          selecting={selectingDestination}
          onSelect={selectDestination}
          onClear={clearDestination}
        />

        <div className="journey-planner__status">
          {status}
        </div>

        <div className="journey-planner__actions">
          <button
            className={
              "journey-planner__button " +
              "journey-planner__button--primary"
            }
            type="button"
            disabled={!canCalculate}
            onClick={calculateRoute}
          >
            Calculate Route
          </button>

          <button
            className="journey-planner__button"
            type="button"
            onClick={clearAll}
          >
            Clear All
          </button>
        </div>
      </div>
    </div>
  );
};

export default JourneyPlanner;