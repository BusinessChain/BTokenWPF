using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public class HeaderDownload
  {
    public List<Header> Locator;

    public Header HeaderTip;
    public Header HeaderRoot; // The first header that is inserted into HeaderDownload
    public Header HeaderAncestor;


    public HeaderDownload(List<Header> locator)
    {
      Locator = locator;
    }

    public virtual void InsertHeader(Header header)
    {
      if (HeaderAncestor == null)
      {
        HeaderAncestor ??= Locator.Find(h => h.Hash.IsAllBytesEqual(header.HashPrevious))
          ?? throw new ProtocolException($"Header {header} does not connect to locator.");

        header.AppendToHeader(HeaderAncestor);
        HeaderRoot = header;
        HeaderTip = header;
      }
      else
      {
        // Falls header schon dran ist, testen ob duplikat nicht erlaubt ist anhand Locator
        // und HeaderAncestor/Root überschreiben.
        header.AppendToHeader(HeaderTip);
        HeaderTip.HeaderNext = header;
        HeaderTip = header;
      }
    }

    public override string ToString()
    {
      return $"{Locator.First()} ... {Locator.Last()}";
    }
  }
}
