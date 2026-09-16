import catalog from '../../../../data/catalog.json';
import { windowsFeed } from '../../../lib/catalog.mjs';
export function GET() {
  return new Response(JSON.stringify(windowsFeed(catalog.entries)), { headers: { 'Content-Type': 'application/json; charset=utf-8' } });
}
