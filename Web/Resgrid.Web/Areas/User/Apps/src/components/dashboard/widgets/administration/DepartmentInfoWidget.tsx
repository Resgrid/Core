import { ItemList } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/DepartmentInfoWidgetData.cs */
export interface DepartmentInfoWidgetData {
  DepartmentId: number;
  DepartmentCode?: string | null;
  TextToCallNumber?: string | null;
}

/** department.info — the reference widget (plan §7.11): today's Department Info ibox. */
export default function DepartmentInfoWidget({ data, t }: WidgetProps<DepartmentInfoWidgetData>) {
  const notSet = <span className="rg-mu">{t('W.department.info.NotSet')}</span>;
  return (
    <ItemList
      items={[
        { key: 'id', title: t('W.department.info.DepartmentId'), right: <b className="rg-num">{data.DepartmentId}</b> },
        { key: 'code', title: t('W.department.info.DepartmentCode'), right: data.DepartmentCode ? <b className="rg-code">{data.DepartmentCode}</b> : notSet },
        {
          key: 'ttc',
          title: t('W.department.info.TextToCall'),
          right: data.TextToCallNumber ? (
            <a href={`sms:${data.TextToCallNumber.replace(/[^\d+]/g, '')}`} dir="ltr">
              {data.TextToCallNumber}
            </a>
          ) : (
            notSet
          ),
        },
      ]}
    />
  );
}
