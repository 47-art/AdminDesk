// Renderer coverage check.
//
// Compares three things and exits 1 when they disagree:
//   1. the field type constants in the frontend,
//   2. the field types and lookup kinds used by the sample definition in scripts/fixtures/fieldtypes,
//   3. the @case branches of the form renderer.
//
// Usage: node scripts/check-field-types.mjs [path-to-renderer-file]
// The renderer path defaults to the dynamic form component.

import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const constantsFile = join(root, 'frontend/src/app/core/constants/field-types.ts');
const sampleFile = join(root, 'scripts/fixtures/fieldtypes/fieldcheck.json');
const rendererFile = process.argv[2]
  ? resolve(process.argv[2])
  : join(root, 'frontend/src/app/features/new-request/dynamic-form.component.ts');

const capitalise = (text) => text.charAt(0).toUpperCase() + text.slice(1);
const problems = [];

// 1. Constants: the values of FIELD_TYPES and the entries of LOOKUP_KINDS.
const constants = readFileSync(constantsFile, 'utf8');
const typesBlock = constants.match(/export const FIELD_TYPES\s*=\s*\{([\s\S]*?)\}\s*as const/);
if (!typesBlock) {
  console.error('Could not read FIELD_TYPES from ' + constantsFile);
  process.exit(1);
}
const constantTypes = [...typesBlock[1].matchAll(/(\w+)\s*:\s*'(\w+)'/g)].map((m) => m[2]);

const kindsBlock = constants.match(/export const LOOKUP_KINDS[^=]*=\s*\[([\s\S]*?)\]/);
if (!kindsBlock) {
  console.error('Could not read LOOKUP_KINDS from ' + constantsFile);
  process.exit(1);
}
const constantKinds = [...kindsBlock[1].matchAll(/'(\w+)'/g)].map((m) => m[1]);

// 2. The sample definition.
const sample = JSON.parse(readFileSync(sampleFile, 'utf8'));
const sampleFields = sample.fields ?? [];
const sampleTypes = new Set(sampleFields.map((f) => capitalise(String(f.type))));
const sampleKinds = new Set(sampleFields.filter((f) => f.type === 'lookup').map((f) => String(f.lookupKind)));

// 3. The renderer.
const renderer = readFileSync(rendererFile, 'utf8');
const cases = new Set();
for (const m of renderer.matchAll(/@case\s*\(\s*(?:FIELD_TYPES\.(\w+)|'(\w+)')\s*\)/g)) {
  cases.add(m[1] ?? m[2]);
}

for (const type of sampleTypes) {
  if (!cases.has(type)) problems.push(`Field type ${type} is used in the sample but the renderer has no @case for it`);
}
for (const type of constantTypes) {
  if (!sampleTypes.has(type)) problems.push(`Field type ${type} is defined as a constant but has no sample field`);
  if (!cases.has(type)) problems.push(`Field type ${type} is defined as a constant but the renderer has no @case for it`);
}
for (const kind of constantKinds) {
  if (!sampleKinds.has(kind)) problems.push(`Lookup kind ${kind} has no sample lookup field`);
}
for (const kind of sampleKinds) {
  if (!constantKinds.includes(kind)) problems.push(`Lookup kind ${kind} is used in the sample but is not a registered kind`);
}

if (problems.length > 0) {
  console.error('Field type check failed:');
  for (const problem of [...new Set(problems)]) console.error('  - ' + problem);
  process.exit(1);
}

console.log(`field types covered: ${constantTypes.length} types, ${constantKinds.length} lookup kinds`);
