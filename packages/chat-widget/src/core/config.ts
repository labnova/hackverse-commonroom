import type { ConfigureFunction, Configuration } from "../types";
import type { TokenStore } from "./tokens";

/** Resolves window[name], calls it synchronously with the token callback and validates the result. */
export function resolveConfiguration(name: string | null, tokens: TokenStore): Configuration {
  if (!name) {
    throw new Error('<commonroom-chat> requires a configure="functionName" attribute.');
  }
  const fn = (window as unknown as Record<string, unknown>)[name];
  if (typeof fn !== "function") {
    throw new Error(`<commonroom-chat>: window.${name} is not a function.`);
  }
  const config = (fn as ConfigureFunction)((token) => tokens.set(token));
  if (!config || typeof config !== "object") {
    throw new Error(`<commonroom-chat>: window.${name}() must return a Configuration object.`);
  }
  if (typeof config.api_key !== "string" || !config.api_key) {
    throw new Error("<commonroom-chat>: Configuration.api_key is required.");
  }
  return config;
}
