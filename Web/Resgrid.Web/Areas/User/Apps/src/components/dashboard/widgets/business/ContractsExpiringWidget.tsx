import { EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import type { WidgetProps } from '../types';
import { enc, rowsFor, toneIf } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/ContractsExpiringWidgetData.cs */
export interface ContractsExpiringWidgetData {
  Days: number;
  Active: number;
  ExpiringCount: number;
  Rows: ContractsExpiringRow[];
  ComplianceDocumentsExpired: number;
  ComplianceDocumentsExpiring: number;
}

export interface ContractsExpiringRow {
  ServiceContractId: string;
  Name?: string | null;
  ContractNumber?: string | null;
  EndOn: string;
  DaysLeft: number;
}

const LIST_URL = '/User/Contracts';

/** contracts.expiring (plan §7.9): active contracts, those ending soon, and compliance documents lapsing. */
export default function ContractsExpiringWidget({ data, size, t, ctx }: WidgetProps<ContractsExpiringWidgetData>) {
  const k = 'W.contracts.expiring.';
  const documentIssues = data.ComplianceDocumentsExpired + data.ComplianceDocumentsExpiring;
  if (!data.Active && !documentIssues) {
    return <EmptyState icon="fa-file-text" text={t(`${k}NoActive`)} linkText={t(`${k}OpenContracts`)} href={LIST_URL} />;
  }

  const medium = size === 'm';
  const rows = medium ? (data.Rows ?? []).slice(0, 2) : rowsFor(data.Rows, size, 2);
  const kpis = [
    { value: ctx.format.number(data.Active, 0), label: t(`${k}Active`), href: LIST_URL },
    { value: ctx.format.number(data.ExpiringCount, 0), label: t(`${k}Expiring`), tone: toneIf(data.ExpiringCount, 'warn') },
  ];
  if (!medium) {
    kpis.push(
      { value: ctx.format.number(data.ComplianceDocumentsExpired, 0), label: t(`${k}DocumentsExpired`), tone: toneIf(data.ComplianceDocumentsExpired, 'bad') },
      { value: ctx.format.number(data.ComplianceDocumentsExpiring, 0), label: t(`${k}DocumentsExpiring`), tone: toneIf(data.ComplianceDocumentsExpiring, 'warn') },
    );
  }

  return (
    <>
      <KpiGrid columns={kpis.length} items={kpis} />
      {medium && documentIssues ? (
        <Note>
          <i className={`fa ${data.ComplianceDocumentsExpired ? 'fa-exclamation-circle' : 'fa-exclamation-triangle'}`} aria-hidden="true" />{' '}
          {t(`${k}DocumentsSummary`, data.ComplianceDocumentsExpired, data.ComplianceDocumentsExpiring)}
        </Note>
      ) : null}
      {rows.length > 0 ? (
        <ItemList
          items={rows.map((row) => ({
            key: row.ServiceContractId,
            title: row.Name || row.ContractNumber || row.ServiceContractId,
            meta: [row.ContractNumber && row.Name ? row.ContractNumber : null, ctx.format.date(row.EndOn)].filter(Boolean).join(' · '),
            tone: row.DaysLeft <= 14 ? 'warn' : undefined,
            right: <span className="rg-num">{t(`${k}EndsIn`, ctx.format.duration(Math.max(0, row.DaysLeft) * 1440))}</span>,
            href: `/User/Contracts/View?id=${enc(row.ServiceContractId)}`,
          }))}
        />
      ) : (
        <Note>{t(`${k}NoneEnding`, ctx.format.duration(data.Days * 1440))}</Note>
      )}
    </>
  );
}
