using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace VehicleServiceManagement.Services.Invoicing;

public interface IInvoicePdfGenerator
{
    byte[] Generate(InvoiceDetailsDto invoice);
}

/// <summary>Renders a printable A4 invoice with QuestPDF (Community licence).</summary>
public sealed class InvoicePdfGenerator : IInvoicePdfGenerator
{
    public byte[] Generate(InvoiceDetailsDto invoice)
    {
        var culture = CultureInfo.GetCultureInfo("en-IN");

        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Invoice #{invoice.Id:D6}").FontSize(18).Bold();
                    col.Item().Text($"Issued {invoice.IssuedOn.ToString("dd MMM yyyy", culture)}   Due {invoice.DueDate.ToString("dd MMM yyyy", culture)}");
                });

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Item().Text($"Billed to: {invoice.CustomerName}");
                    col.Item().Text($"Vehicle: {invoice.VehicleRegistration}");

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Text("Description").Bold();
                            h.Cell().AlignRight().Text("Qty").Bold();
                            h.Cell().AlignRight().Text("Rate").Bold();
                            h.Cell().AlignRight().Text("Amount").Bold();
                        });

                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().Text(line.Description);
                            table.Cell().AlignRight().Text(line.Quantity.ToString(culture));
                            table.Cell().AlignRight().Text(line.UnitPrice.ToString("N2", culture));
                            table.Cell().AlignRight().Text(line.Amount.ToString("N2", culture));
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"Subtotal: {invoice.Subtotal.ToString("N2", culture)}");

                        if (invoice.DiscountAmount > 0)
                            totals.Item().Text($"Discount ({invoice.DiscountPercent}%): -{invoice.DiscountAmount.ToString("N2", culture)}");

                        if (invoice.IgstAmount > 0)
                        {
                            totals.Item().Text($"IGST ({invoice.TaxRate * 100:0.##}%): {invoice.IgstAmount.ToString("N2", culture)}");
                        }
                        else
                        {
                            var halfRate = invoice.TaxRate / 2m * 100m;
                            totals.Item().Text($"CGST ({halfRate:0.##}%): {invoice.CgstAmount.ToString("N2", culture)}");
                            totals.Item().Text($"SGST ({halfRate:0.##}%): {invoice.SgstAmount.ToString("N2", culture)}");
                        }

                        totals.Item().Text($"Total: {invoice.Total.ToString("N2", culture)}").Bold().FontSize(12);
                        totals.Item().Text($"Paid: {invoice.AmountPaid.ToString("N2", culture)}   Balance: {invoice.Balance.ToString("N2", culture)}");
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf();
    }
}
