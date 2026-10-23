export function takePersonaChoice(current: { ticket: number }): number {
  current.ticket += 1;
  return current.ticket;
}

export function isCurrentPersonaChoice(ticket: number, current: { ticket: number }): boolean {
  return ticket === current.ticket;
}
