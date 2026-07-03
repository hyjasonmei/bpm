using Microsoft.OData.Edm;

namespace Bpm.Admin.Api.Odata;

/// <summary>SPIKE (1.1b): one OData entity set per custom dataset, columns from its schema.</summary>
public sealed record DatasetDef(string SetName, string EntityName, IReadOnlyList<string> ColumnKeys);

/// <summary>
/// Builds a DYNAMIC EDM from the customer's datasets — each dataset becomes its
/// own entity set with real string properties (from ColumnsJson), so Power BI /
/// Excel see one table per dataset and can $filter a single column. Uses the raw
/// EdmModel API (ODataConventionModelBuilder needs CLR types, which we don't have
/// for dynamic schemas).
/// </summary>
public static class DatasetEdmModel
{
    public const string Namespace = "Ds";

    public static IEdmModel Build(IReadOnlyList<DatasetDef> datasets)
    {
        var model = new EdmModel();
        var container = new EdmEntityContainer(Namespace, "Container");
        model.AddElement(container);

        foreach (var ds in datasets)
        {
            var et = new EdmEntityType(Namespace, ds.EntityName);
            var id = et.AddStructuralProperty("Id", EdmPrimitiveTypeKind.Guid, isNullable: false);
            et.AddKeys(id);
            foreach (var col in ds.ColumnKeys)
                et.AddStructuralProperty(col, EdmCoreModel.Instance.GetString(isNullable: true));
            model.AddElement(et);
            container.AddEntitySet(ds.SetName, et);
        }
        return model;
    }

    /// <summary>OData identifiers can't contain '-' etc; slugify a dataset key.</summary>
    public static string SafeName(string key)
    {
        var chars = key.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        return char.IsLetter(s.FirstOrDefault()) ? s : "ds_" + s;
    }
}
