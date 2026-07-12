export function shouldResetComposerForSessionChange(
  previousCommittedSessionId: string | null,
  nextCommittedSessionId: string | null,
) {
  return (
    previousCommittedSessionId !== null &&
    nextCommittedSessionId !== null &&
    previousCommittedSessionId !== nextCommittedSessionId
  )
}
