import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const artifactUrl = new URL(
  '../../rds/tasks/commissioning/TKG_SIM_DYNAMIC_MOVE.task',
  import.meta.url,
);

async function loadArtifact() {
  const bytes = await readFile(artifactUrl);
  const raw = bytes.toString('utf8');
  const outer = JSON.parse(raw);

  assert.ok(Array.isArray(outer), 'RDS export must use an outer task array');
  assert.equal(outer.length, 1, 'RDS export must contain exactly one task');

  const task = outer[0];
  const detail = JSON.parse(task.detail);

  return { bytes, raw, task, detail };
}

function flattenBlocks(rootBlock) {
  const result = [];
  const visit = (block) => {
    result.push(block);
    for (const branch of Object.values(block.children ?? {})) {
      for (const child of branch) {
        visit(child);
      }
    }
  };

  visit(rootBlock);
  return result;
}

test('RDS export contains the canonical dynamic task-level input contract', async () => {
  const { bytes, task, detail } = await loadArtifact();

  assert.equal(bytes.length, 1744);
  assert.equal(
    createHash('sha256').update(bytes).digest('hex'),
    '49abed5e2f36feadeb2ad7b8e9775e095b1055b75599c8b36a1486cf7d0de1d3',
    'checked-in bytes must remain identical to the RDS batch export response',
  );

  assert.equal(task.label, 'TKG_SIM_DYNAMIC_MOVE');
  assert.equal(task.id, '1c434014-e1c0-4237-8724-e670972d905f');
  assert.equal(task.version, 2);
  assert.equal(
    task.remark,
    'Development-only dynamic simulator commissioning. Not for production transport.',
  );

  assert.deepEqual(
    detail.inputParams.map(({ name, type, required, defaultValue }) => ({
      name,
      type,
      required,
      defaultValue,
    })),
    [
      { name: 'from', type: 'String', required: true, defaultValue: '' },
      { name: 'to', type: 'String', required: true, defaultValue: '' },
    ],
  );
  assert.deepEqual(detail.outputParams, []);
});

test('RDS-native expression serializer binds from and to to the expected block fields', async () => {
  const { detail } = await loadArtifact();
  const blocks = flattenBlocks(detail.rootBlock);
  const selectBlocks = blocks.filter(({ blockType }) => blockType === 'CSelectAgvBp');
  const actionBlocks = blocks.filter(({ blockType }) => blockType === 'CAgvOperationBp');

  assert.equal(detail.rootBlock.blockType, 'RootBp');
  assert.equal(selectBlocks.length, 1);
  assert.equal(actionBlocks.length, 2);

  const select = selectBlocks[0];
  assert.deepEqual(select.inputParams.vehicle, {
    type: 'Simple',
    value: '',
    required: true,
  });
  assert.deepEqual(select.inputParams.keyRoute, {
    type: 'Expression',
    value: 'taskInputs.from',
    required: true,
  });

  assert.deepEqual(
    actionBlocks.map(({ inputParams }) => inputParams.targetSiteLabel),
    [
      { type: 'Expression', value: 'taskInputs.from', required: true },
      { type: 'Expression', value: 'taskInputs.to', required: true },
    ],
  );
  assert.deepEqual(
    actionBlocks.map(({ inputParams }) => inputParams.scriptName),
    [
      { type: 'Simple', value: 'Wait' },
      { type: 'Simple', value: 'Wait' },
    ],
  );
});

test('commissioning task has no fixed robot, fixed route values, or mutation/device blocks', async () => {
  const { raw, detail } = await loadArtifact();
  const blocks = flattenBlocks(detail.rootBlock);
  const blockTypes = blocks.map(({ blockType }) => blockType);
  const parameterValues = blocks.flatMap(({ inputParams = {} }) =>
    Object.values(inputParams).map(({ value }) => value),
  );

  assert.deepEqual(blockTypes, ['RootBp', 'CSelectAgvBp', 'CAgvOperationBp', 'CAgvOperationBp']);
  assert.ok(!parameterValues.includes('sim_01'));
  assert.ok(!parameterValues.includes('sim_02'));
  assert.ok(!parameterValues.includes('AP15'));
  assert.ok(!parameterValues.includes('AP20'));

  for (const blockType of blockTypes) {
    assert.doesNotMatch(
      blockType,
      /work.?site|content|filled|lock|unlock|device|plc|modbus|fork|load|unload|bin.?task/i,
    );
  }

  assert.doesNotMatch(raw, /integration.?key|callback.?key|authorization|bearer|password|jwt/i);
});
