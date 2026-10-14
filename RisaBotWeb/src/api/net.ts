/**
 * Well-known addresses, with no dependencies.
 *
 * This is a leaf module on purpose. Both the network layer (discovery) and a
 * pure display component (Secret) need the hotspot address, and a display
 * primitive should not have to import the whole API client to learn one string.
 */

/**
 * The gateway of the hotspot the robot broadcasts on ap0.
 *
 * Fixed by the robot's own dnsmasq/hostapd config, and therefore identical on
 * every RISA-Bot — which is exactly why it is the one address the UI shows in
 * full rather than masking. See PUBLIC_ADDRESSES in components/Secret.tsx.
 */
export const HOTSPOT_IP = '192.168.4.1'
