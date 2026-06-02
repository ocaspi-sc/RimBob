import type { BlueprintAsset, BlueprintGroup } from '../../types/advice';

export function BlueprintFootprintThumbnail({ group }: { group: BlueprintGroup }) {
  if (group.assets.length === 0) {
    return <div className="blueprint-thumbnail empty">No footprint</div>;
  }

  const assets = [...group.assets].sort(compareAssetLayer);
  const xs = assets.map(asset => asset.cell.x);
  const zs = assets.map(asset => asset.cell.z);
  const minX = Math.min(...xs);
  const minZ = Math.min(...zs);
  const maxX = Math.max(...xs);
  const maxZ = Math.max(...zs);
  const width = maxX - minX + 1;
  const height = maxZ - minZ + 1;
  const cell = 12;
  const gap = 1;
  const svgWidth = width * cell + Math.max(0, width - 1) * gap;
  const svgHeight = height * cell + Math.max(0, height - 1) * gap;
  const roles = [...new Set(assets.map(asset => normalizeRole(asset.role)))].sort(compareRole);

  return (
    <div className="blueprint-thumbnail" aria-label={`${group.label} footprint`}>
      <svg viewBox={`0 0 ${svgWidth} ${svgHeight}`} role="img" aria-label={`${group.assets.length} blueprint assets on map ${group.map_id}`}>
        <title>{group.label}</title>
        {assets.map((asset, index) => (
          <rect
            key={`${asset.role}-${asset.def_name}-${asset.cell.x}-${asset.cell.z}-${asset.rotation}-${index}`}
            x={(asset.cell.x - minX) * (cell + gap)}
            y={(asset.cell.z - minZ) * (cell + gap)}
            width={cell}
            height={cell}
            rx="2"
            fill={roleColor(asset.role)}
          />
        ))}
      </svg>
      <div className="blueprint-thumbnail-legend">
        {roles.map(role => (
          <span key={role}>
            <i style={{ background: roleColor(role) }} aria-hidden />
            {role}
          </span>
        ))}
      </div>
    </div>
  );
}

function compareAssetLayer(left: BlueprintAsset, right: BlueprintAsset): number {
  return compareRole(normalizeRole(left.role), normalizeRole(right.role)) ||
    left.cell.z - right.cell.z ||
    left.cell.x - right.cell.x;
}

function compareRole(left: string, right: string): number {
  return roleOrder(left) - roleOrder(right) || left.localeCompare(right);
}

function roleOrder(role: string): number {
  switch (normalizeRole(role)) {
    case 'floor':
      return 0;
    case 'wall':
      return 1;
    case 'door':
      return 2;
    case 'cooler':
      return 3;
    default:
      return 4;
  }
}

function roleColor(role: string): string {
  switch (normalizeRole(role)) {
    case 'floor':
      return '#6f7f5f';
    case 'wall':
      return '#b7b0a4';
    case 'door':
      return '#d59a52';
    case 'cooler':
      return '#62a8c7';
    case 'heater':
      return '#c87854';
    case 'vent':
      return '#88a7a0';
    default:
      return '#8b92a5';
  }
}

function normalizeRole(role: string): string {
  return role.trim().replace(/_/g, ' ').toLowerCase() || 'asset';
}
