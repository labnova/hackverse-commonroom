import type { Token } from "../types";

/**
 * Holds the latest host token. undefined = not received yet, null = host reported auth failure.
 */
export class TokenStore {
  private token: Token | null | undefined = undefined;
  private readonly listeners = new Set<() => void>();

  get = (): Token | null | undefined => this.token;

  set = (token: Token | undefined): void => {
    this.token = token && token.access_token ? token : null;
    for (const listener of this.listeners) listener();
  };

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };
}
