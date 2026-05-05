using System.Data;

namespace GV23_Notice.Models
{
    public class DataRowWrapper
    {
        public DataRow Row { get; set; }

        public DataRowWrapper(DataRow row)
        {
            Row = row;
        }
    }
}
