using Microsoft.Extensions.Logging;
using QuickFix.Fields;
using QuickFix.FIX50SP2;
using SoftWell.RtFix.ConsoleHost.Scenarios.Infrastructure;

namespace SoftWell.RtFix.ConsoleHost.Scenarios;

public class SendQuotationsRequestReceiveRefreshed : QuotationScenarioBase
{
    public SendQuotationsRequestReceiveRefreshed(
        ScenarioSettings settings,
        ILoggerFactory loggerFactory) : base(settings, loggerFactory)
    {
    }

    public override string Name => nameof(SendQuotationsRequestReceiveRefreshed);

    public override string? Description => "Отправить запрос на котировки, получить обновление";

    protected override async Task RunAsyncInner(ScenarioContext context, CancellationToken ct)
    {
        var request = Helpers.CreateQuotationRequest(new[] { QuotationSecurityId }, null);
        context.Client.SendMessage(request);

        Logger.LogInformation("Отправили запрос на котировки, ожидаем хотя бы одно обновление котировки..");

        await foreach (var msg in context.Client.ReadAllMessagesAsync(ct))
        {
            if (msg.IsOfType<MarketDataIncrementalRefresh>(MsgType.MARKET_DATA_INCREMENTAL_REFRESH, out var mdir))
            {
                LogMarketDataIncrementalRefresh(mdir);
                return;
            }
            else if (msg.IsOfType<MarketDataRequestReject>(MsgType.MARKET_DATA_REQUEST_REJECT, out var mdrr))
            {
                var reason = mdrr.MDReqRejReason.getValue();
                if (reason == MDReqRejReason.UNKNOWN_SYMBOL)
                {
                    Logger.LogWarning("На запрос на получение котировок сервер ответил следующими предупреждениями: {warnings}", mdrr.Text.getValue());
                }
                else
                {
                    throw new Exception($"Запрос на получение котировок был отклонен с причиной {reason}: {mdrr.Text.getValue()}");
                }
            }
            else if (
                msg.IsOfType<BusinessMessageReject>(MsgType.BUSINESS_MESSAGE_REJECT, out var bmr)
                && bmr.RefMsgType.getValue() == MsgType.MARKET_DATA_REQUEST
                && bmr.IsSetBusinessRejectRefID()
                && bmr.BusinessRejectRefID.getValue() == request.MDReqID.getValue())
            {
                throw new Exception($"Запрос отклонен с причиной {bmr.BusinessRejectReason.getValue()}: {bmr.Text.getValue()}");
            }
        }
    }

    private void LogMarketDataIncrementalRefresh(MarketDataIncrementalRefresh mdr)
    {
        var msgs = new List<string>();

        for (var i = 1; i <= mdr.NoMDEntries.getValue(); i++)
        {
            var g = new MarketDataIncrementalRefresh.NoMDEntriesGroup();
            mdr.GetGroup(i, g);

            var pg = new MarketDataIncrementalRefresh.NoMDEntriesGroup.NoPartyIDsGroup();

            g.GetGroup(1, pg);

            if (g.MDUpdateAction.getValue() == MDUpdateAction.NEW)
            {
                msgs.Add($"инструмент {g.SecurityID.getValue()} NEW {g.MDEntryType.getValue()}: {g.MDEntryPx.getValue()}, PartyId={pg.PartyID.getValue()}");
            }
            else if (g.MDUpdateAction.getValue() == MDUpdateAction.DELETE)
            {
                msgs.Add($"инструмент {g.SecurityID.getValue()} DELETE {g.MDEntryType.getValue()}: PartyId={pg.PartyID.getValue()}");
            }
            else throw new InvalidOperationException("Unknown MDUpdateAction " + g.MDUpdateAction.getValue());
        }

        Logger.LogInformation(@"{date}: Получили обновление котировок: 
{prices}",
            DateTime.Now,
            msgs);
    }
}