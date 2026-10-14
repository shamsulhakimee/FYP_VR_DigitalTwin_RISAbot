/**
 * Check the address-masking rules against the real source, not a copy.
 *
 * The policy has one deliberate hole in it — the hotspot gateway is shown in
 * full — and a hole is exactly the kind of thing that quietly widens. This
 * pins both halves: what must stay hidden, and the single address that must
 * not be.
 *
 *   node scripts/check-masking.mjs
 *
 * jiti is used because these live in a .tsx file; it is already a dependency of
 * the toolchain, so this needs no test runner and no build step.
 */
import { createJiti } from 'jiti'

const jiti = createJiti(import.meta.url)
const { maskAddresses, isMaskedAddress, isIpLiteral } = await jiti.import(
  '../src/components/mask.ts',
)

const DOTS = '•••.•••.•••.•••'

const cases = [
  // [input, expected masked output, why]
  ['192.168.4.1', '192.168.4.1', 'hotspot gateway - shown, it is the same on every robot'],
  ['http://192.168.4.1:8080', 'http://192.168.4.1:8080', 'hotspot URL - shown whole'],
  ['192.168.68.54', DOTS, 'LAN address - hidden, says which network the robot is on'],
  ['http://192.168.68.54:8080', `http://${DOTS}:8080`, 'LAN URL - URL shape kept'],
  ['10.0.0.42', DOTS, 'the demo address is hidden like any other'],
  ['192.168.4.10', DOTS, 'a near-miss on the gateway is NOT exempt'],
  ['192.168.41.1', DOTS, 'a different network that merely looks similar is hidden'],
  ['192.168.4.100', DOTS, 'another host on the hotspot subnet is hidden - only .1 is exempt'],
  // Two shapes that are not addresses at all. Neither is a leak, and both are
  // pinned here so nobody "fixes" them into something more complicated:
  //   - `1192.168.4.1` has no word boundary to start on, so nothing matches.
  //   - `192.168.4.1.5` does match the exempt token, then leaves a stray `.5`.
  //     The only thing shown is the address we deliberately show anyway.
  ['1192.168.4.1', '1192.168.4.1', 'no valid address present, so nothing is masked'],
  ['192.168.4.1.5', '192.168.4.1.5', 'exempt token shown; the trailing .5 is not an address'],
  ['risabot.internal', 'risabot.internal', 'names are never touched'],
  ['risabot10.local', 'risabot10.local', 'names are never touched'],
  ['joined 192.168.4.1 then 192.168.68.54', `joined 192.168.4.1 then ${DOTS}`, 'mixed in prose'],
]

let failed = 0
for (const [input, expected, why] of cases) {
  const got = maskAddresses(input)
  const ok = got === expected
  if (!ok) failed++
  console.log(`${ok ? 'pass' : 'FAIL'}  ${input}`)
  console.log(`        -> ${got}${ok ? '' : `   (expected ${expected})`}`)
  console.log(`        ${why}`)
}

// isMaskedAddress drives "show a name instead of numbers" in the header, so it
// has to agree with maskAddresses or the two screens disagree about one robot.
const predicate = [
  ['192.168.4.1', false],
  ['192.168.68.54', true],
  [' 192.168.4.1 ', false],
  ['risabot.internal', false],
  ['999.1.1.1', false],
]
console.log()
for (const [host, expected] of predicate) {
  const got = isMaskedAddress(host)
  const ok = got === expected
  if (!ok) failed++
  console.log(`${ok ? 'pass' : 'FAIL'}  isMaskedAddress(${JSON.stringify(host)}) = ${got}`)
}

// isIpLiteral is the raw predicate and must still answer honestly about the
// gateway; only isMaskedAddress carries the exemption.
if (isIpLiteral('192.168.4.1') !== true) {
  console.log('FAIL  isIpLiteral should still call the gateway an IP literal')
  failed++
}

console.log()
console.log(failed ? `FAILED (${failed})` : 'all masking rules hold')
process.exit(failed ? 1 : 0)
