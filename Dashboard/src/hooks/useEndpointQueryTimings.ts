import { useSyncExternalStore } from 'react';
import {
  getEndpointQueryTimings,
  subscribeEndpointQueryTimings,
  type EndpointQueryTiming,
} from '../api/requestTelemetry';

export function useEndpointQueryTimings(): EndpointQueryTiming[] {
  return useSyncExternalStore(
    subscribeEndpointQueryTimings,
    getEndpointQueryTimings,
    getEndpointQueryTimings,
  );
}
