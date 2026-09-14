// REST uses ASP.NET web JSON (camelCase). SSE uses explicit System.Text.Json defaults;
// its PascalCase envelope is normalized only in workflow-stream.ts.
export interface Target {
  revision: number;
  concurrencyToken: string;
}
export interface Action {
  order: number;
  instruction: string;
}
export interface Content {
  equipmentId: string;
  manualId: string;
  manualRevisionId: string;
  reportedSymptom: string;
  description: string;
  actions: Action[];
}
export interface Requirement {
  id: string;
  description: string;
  mandatory: boolean;
}
export interface VerifiedRequirement extends Requirement {
  status: string;
  verifiedBy: string | null;
  evidence: string | null;
}
export interface Review {
  workOrderId: string;
  target: Target;
  content: Content;
  status: string;
  safetyAssessmentRevision: number | null;
  requirements: VerifiedRequirement[];
  decision: string | null;
}
export interface DecisionResult {
  outcome: string;
  review: Review | null;
}
export interface Preview {
  canProceed: boolean;
  requirements: Requirement[];
}
export interface Citation {
  documentId: string;
  manualRevisionId: string;
  chunkId: string;
  locator: string;
  snippet: string;
}
export interface Evidence {
  workOrderId: string;
  currentRevision: number;
  source: string;
  evidence: Citation[] | null;
}
export interface Run {
  runId: string;
  equipmentId: string;
  symptom: string;
  status: string;
  cancellationRequested: boolean;
  workOrderIds: string[];
  executionIds: string[];
}
export interface WorkflowResult {
  runId: string;
  workOrderId: string | null;
  executionId: string;
  correlationId: string;
  outcome: string;
}
export interface Dispatch {
  attemptId: string | null;
  workOrderId: string | null;
  revision: number | null;
  outcome: string;
  state: string | null;
  externalReference: string | null;
  failure: string | null;
}
export interface Trace {
  executionId: string;
  correlationId: string;
  runId: string | null;
  steps: {
    kind: string;
    name: string;
    status: string;
    error: string | null;
    startedAt: string;
    completedAt: string | null;
  }[];
}
export type RecordKind = 'runs' | 'work-orders' | 'dispatch' | 'traces';
export interface Activity {
  kind: RecordKind;
  id: string;
  status: string;
  observedAt: string;
}
