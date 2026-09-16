import catalog from '../../../../data/catalog.json';
export function GET() {
  return new Response(JSON.stringify(catalog), { headers: { 'Content-Type': 'application/json; charset=utf-8' } });
}
