"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const integration = require("../tkg_integration.js");

function fixture(overrides = {}) {
  const state = { handlers: {}, events: {}, creates: [], callbacks: [], updates: [], logs: [], records: new Map() };
  const jj = {
    registerHandler(method, path, callbackName, auth) { state.handlers[`${method} ${path}`] = { callbackName, auth }; },
    registerTaskEventFunction(name) { state.events[name] = name; },
    isTaskLabelExist(label) { return label === "TKG_SIM_TRANSPORT"; },
    isTaskRecordIdExist(id) { return state.records.has(id); },
    getTaskRecordById(id) { return state.records.get(id); },
    createWindTask(payload) { state.creates.push(JSON.parse(payload)); return JSON.stringify({ code: 200, msg: "Success" }); },
    requestHttpPost(url, body, headers, mediaType) { state.callbacks.push({ url, body: JSON.parse(body), headers: JSON.parse(headers), mediaType }); return "ok"; },
    updateSitesByCondition(conditions, values) { state.updates.push({ conditions: JSON.parse(conditions), values: JSON.parse(values) }); },
    log(value) { state.logs.push(value); },
    ...overrides
  };
  const config = { integrationKey: "integration-secret", callbackUrl: "http://127.0.0.1/callback", callbackKey: "callback-secret" };
  integration.install(jj, config);
  const call = (path, body) => {
    const envelope = integration.invokeHttp(jj, config, state.handlers[`POST ${path}`].callbackName, JSON.stringify(body));
    assert.equal(envelope.code, 200);
    return JSON.parse(envelope.body);
  };
  return { state, call, jj, config };
}

const valid = { integrationKey: "integration-secret", taskRecordId: "request-1", taskLabel: "TKG_SIM_TRANSPORT", priority: 42, inputParams: { fromSite: "A", toSite: "B" } };

test("invalid integration key is rejected and never echoed", () => {
  const { call } = fixture();
  const response = call("/script-api/tkg/create-transport", { ...valid, integrationKey: "wrong" });
  assert.equal(response.code, 401);
  assert.doesNotMatch(JSON.stringify(response), /integration-secret|wrong/);
});

test("missing task label and unknown task label are rejected", () => {
  const { call } = fixture();
  assert.equal(call("/script-api/tkg/create-transport", { ...valid, taskLabel: "" }).code, 422);
  assert.equal(call("/script-api/tkg/create-transport", { ...valid, taskLabel: "UNKNOWN" }).code, 404);
});

test("missing input structure is rejected before task creation", () => {
  const { call, state } = fixture();
  assert.equal(call("/script-api/tkg/create-transport", { ...valid, inputParams: null }).code, 422);
  assert.equal(state.creates.length, 0);
});

test("new identity creates exactly once and preserves priority/input params", () => {
  const { call, state } = fixture();
  const response = call("/script-api/tkg/create-transport", valid);
  assert.equal(response.code, 200);
  assert.equal(state.creates.length, 1);
  assert.deepEqual(state.creates[0], { taskRecordId: "request-1", taskLabel: "TKG_SIM_TRANSPORT", priority: 42, inputParams: JSON.stringify({ fromSite: "A", toSite: "B" }) });
});

test("existing identity is an idempotent replay and creates no duplicate", () => {
  const { call, state } = fixture();
  state.records.set("request-1", { id: "request-1", status: 1000 });
  const response = call("/script-api/tkg/create-transport", valid);
  assert.equal(response.data.idempotentReplay, true);
  assert.equal(state.creates.length, 0);
  assert.equal(response.data.taskRecord.id, "request-1");
});

test("script handlers use POST with RDS authentication disabled in favor of the integration key", () => {
  const { state } = fixture();
  for (const registration of Object.values(state.handlers)) assert.equal(registration.auth, false);
});

test("integration and callback secrets are never logged", () => {
  const { call, state, jj, config } = fixture();
  call("/script-api/tkg/validate-transport", valid);
  integration.forwardEvent(jj, config, state.events.taskDone, JSON.stringify({ taskRecordId: "request-1" }));
  assert.deepEqual(state.logs, []);
});

test("validation reports task definition and identity state without creating", () => {
  const { call, state } = fixture();
  const response = call("/script-api/tkg/validate-transport", valid);
  assert.equal(response.data.validated, true);
  assert.equal(response.data.taskLabelExists, true);
  assert.equal(response.data.taskRecordExists, false);
  assert.equal(state.creates.length, 0);
});

test("validation reports an existing identity as non-new without creating", () => {
  const { call, state } = fixture();
  state.records.set("request-1", { id: "request-1", status: 1000 });
  const response = call("/script-api/tkg/validate-transport", valid);
  assert.equal(response.data.validated, false);
  assert.equal(response.data.taskRecordExists, true);
  assert.equal(state.creates.length, 0);
});

for (const eventType of ["taskDone", "taskFailed", "taskStopped", "workSiteChanged"]) {
  test(`${eventType} forwards raw evidence with callback key header`, () => {
    const { state, jj, config } = fixture();
    integration.forwardEvent(jj, config, state.events[eventType], JSON.stringify({ taskRecordId: "request-1", status: 1003 }));
    assert.equal(state.callbacks.length, 1);
    assert.equal(state.callbacks[0].body.eventType, eventType);
    assert.equal(state.callbacks[0].body.rawPayload.taskRecordId, "request-1");
    assert.equal(state.callbacks[0].headers["X-TKG-RDS-Key"], "callback-secret");
    assert.equal(state.callbacks[0].mediaType, "JSON");
  });
}

test("content helper changes only content", () => {
  const { call, state } = fixture();
  const response = call("/script-api/tkg/update-content", { integrationKey: "integration-secret", siteId: "WS-01", content: "FULL" });
  assert.equal(response.data.filledChanged, false);
  assert.deepEqual(state.updates, [{ conditions: { siteIds: ["WS-01"] }, values: { content: "FULL" } }]);
  assert.equal(JSON.stringify(state.updates).includes("filled"), false);
});
